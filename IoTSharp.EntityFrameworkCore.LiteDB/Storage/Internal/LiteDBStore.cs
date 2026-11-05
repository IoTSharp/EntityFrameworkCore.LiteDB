using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed partial class LiteDBStore : ILiteDBStore, IDisposable
{
    private readonly LiteDatabase _database;
    private readonly object _lock = new();
    private readonly Dictionary<IEntityType, LiteDBTable> _tables = new();
    private Guid? _transactionOwner;
    private int _transactionThread;
    private bool _transactionAborted;

    public LiteDBStore(string connectionString) => _database = new LiteDatabase(connectionString);
    public void Dispose() => _database.Dispose();

    public bool EnsureCreated(IUpdateAdapterFactory updateAdapterFactory, IModel designModel, IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger)
    {
        lock (_lock)
        {
            if (_transactionOwner != null) throw new InvalidOperationException("EnsureCreated cannot run inside an active LiteDB transaction.");
            var marker = _database.GetCollection<BsonDocument>("__iotsharp_ef");
            if (marker.FindById(1) != null) return false;
            if (_database.GetCollectionNames().Any(name => name != "__iotsharp_ef"))
            {
                marker.Upsert(new BsonDocument { ["_id"] = 1, ["provider"] = "IoTSharp.EntityFrameworkCore.LiteDB" });
                return false;
            }
            var ownsTransaction = _database.BeginTrans();
            try
            {
                var adapter = updateAdapterFactory.CreateStandalone(null);
                var entries = new List<IUpdateEntry>();
                foreach (var entityType in designModel.GetEntityTypes())
                {
                    EnsureTable(entityType);
                    foreach (var seed in entityType.GetSeedData())
                    {
                        var entry = adapter.CreateEntry(seed, adapter.Model.FindEntityType(entityType.Name));
                        entry.EntityState = EntityState.Added;
                        entries.Add(entry);
                    }
                }
                ExecuteTransaction(entries, updateLogger);
                marker.Insert(new BsonDocument { ["_id"] = 1, ["provider"] = "IoTSharp.EntityFrameworkCore.LiteDB" });
                if (ownsTransaction) _database.Commit();
                return true;
            }
            catch
            {
                if (ownsTransaction) _database.Rollback();
                _tables.Clear();
                throw;
            }
        }
    }

    public bool Clear()
    {
        lock (_lock)
        {
            if (_transactionOwner != null) throw new InvalidOperationException("EnsureDeleted cannot run inside a transaction.");
            var names = _database.GetCollectionNames().ToArray();
            foreach (var name in names) _database.DropCollection(name);
            _tables.Clear();
            return names.Length != 0;
        }
    }

    public IReadOnlyList<LiteDBTableSnapshot> GetTables(IEntityType entityType, CancellationToken cancellationToken = default, Guid sessionId = default)
    {
        lock (_lock)
        {
            CheckSession(sessionId);
            return entityType.GetConcreteDerivedTypesInclusive()
                .Select(type => new LiteDBTableSnapshot(type, EnsureTable(type).SnapshotRows(cancellationToken))).ToArray();
        }
    }

    public LiteDBIntegerValueGenerator<TProperty> GetIntegerValueGenerator<TProperty>(IProperty property)
    {
        lock (_lock)
        {
            var entity = property.DeclaringType.ContainingEntityType;
            var generator = EnsureTable(entity).GetIntegerValueGenerator<TProperty>(property);
            generator.Bump(entity.GetDerivedTypesInclusive().Select(type => EnsureTable(type).Maximum(property)).DefaultIfEmpty(0).Max());
            return generator;
        }
    }

    public int ExecuteTransaction(IList<IUpdateEntry> entries, IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger, Guid sessionId = default)
    {
        lock (_lock)
        {
            CheckSession(sessionId);
            if (System.Transactions.Transaction.Current != null)
                throw new NotSupportedException("LiteDB cannot enlist in an ambient transaction. See SUPPORT.md (LDB-002).");
            if (_transactionAborted) throw new InvalidOperationException("The LiteDB transaction was rolled back after a failed batch. Dispose or roll it back before continuing.");
            var ownsTransaction = _database.BeginTrans();
            try
            {
                var rows = 0;
                foreach (var entry in entries)
                {
                    if (entry.SharedIdentityEntry != null && entry.EntityState == EntityState.Deleted) continue;
                    var table = EnsureTable(entry.EntityType);
                    if (entry.SharedIdentityEntry != null) table.Delete(entry.SharedIdentityEntry);
                    switch (entry.EntityState)
                    {
                        case EntityState.Added: table.Create(entry); break;
                        case EntityState.Modified: table.Update(entry); break;
                        case EntityState.Deleted: table.Delete(entry); break;
                    }
                    rows++;
                }
                if (ownsTransaction) _database.Commit();
                return rows;
            }
            catch (Exception exception)
            {
                if (ownsTransaction) _database.Rollback();
                else if (_transactionOwner != null)
                {
                    _database.Rollback();
                    _transactionAborted = true;
                }
                if (exception is LiteException) throw new DbUpdateException("LiteDB could not save the batch. See the inner exception.", exception, entries.ToArray());
                throw;
            }
        }
    }

    private LiteDBTable EnsureTable(IEntityType entityType)
    {
        if (!_tables.TryGetValue(entityType, out var table))
        {
            table = new LiteDBTable(_database, entityType);
            _tables.Add(entityType, table);
        }
        return table;
    }

    public IReadOnlyList<object[]> ExecuteQuery(IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.LiteDBQueryPlan plan, object[] parameters, CancellationToken cancellationToken, Guid sessionId)
    {
        lock (_lock)
        {
            CheckSession(sessionId);
            return EnsureTable(plan.EntityType).ExecuteQuery(plan, parameters, cancellationToken);
        }
    }

    public void BeginTransaction(Guid sessionId)
    {
        lock (_lock)
        {
            if (_transactionOwner != null) throw new InvalidOperationException("A LiteDB transaction is already active for this database.");
            if (!_database.BeginTrans()) throw new InvalidOperationException("LiteDB could not begin a new transaction.");
            _transactionOwner = sessionId;
            _transactionThread = Environment.CurrentManagedThreadId;
            _transactionAborted = false;
        }
    }

    public object ExecuteScalar(IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.LiteDBQueryPlan plan, object[] parameters, string operation, Guid sessionId)
    {
        lock (_lock)
        {
            CheckSession(sessionId);
            return EnsureTable(plan.EntityType).ExecuteScalar(plan, parameters, operation);
        }
    }

    public void CommitTransaction(Guid sessionId) => CompleteTransaction(sessionId, true);
    public void RollbackTransaction(Guid sessionId) => CompleteTransaction(sessionId, false);

    private void CompleteTransaction(Guid sessionId, bool commit)
    {
        lock (_lock)
        {
            CheckSession(sessionId);
            if (_transactionOwner == null) throw new InvalidOperationException("There is no active LiteDB transaction.");
            if (commit && _transactionAborted) throw new InvalidOperationException("This LiteDB transaction has been rolled back after a failed batch.");
            if (!_transactionAborted) { if (commit) _database.Commit(); else _database.Rollback(); }
            _transactionOwner = null;
            _transactionAborted = false;
        }
    }

    private void CheckSession(Guid sessionId)
    {
        if (_transactionOwner != null && (_transactionOwner != sessionId || _transactionThread != Environment.CurrentManagedThreadId))
            throw new InvalidOperationException("LiteDB transactions belong to one context and one thread. Complete the active transaction on its original thread.");
    }
}
