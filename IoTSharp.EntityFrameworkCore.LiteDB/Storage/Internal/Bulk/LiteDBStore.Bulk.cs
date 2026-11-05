using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed partial class LiteDBStore
{
    public int ExecuteBulk(IEntityType entityType, Func<IEnumerable<ValueBuffer>> source, IProperty[] properties,
        Func<ValueBuffer, object[]> values, CancellationToken cancellationToken, Guid sessionId)
    {
        lock (_lock)
        {
            CheckSession(sessionId);
            cancellationToken.ThrowIfCancellationRequested();
            if (System.Transactions.Transaction.Current != null)
                throw new NotSupportedException("LiteDB cannot enlist in an ambient transaction. See SUPPORT.md (LDB-002).");
            if (_transactionAborted) throw new InvalidOperationException("The LiteDB transaction was rolled back after a failed batch.");
            var ownsTransaction = _database.BeginTrans();
            try
            {
                // The query and all setters see the pre-command snapshot, inside the write transaction.
                var rows = source().ToArray();
                var affected = EnsureTable(entityType).ExecuteBulk(rows, properties, values, cancellationToken);
                if (ownsTransaction) _database.Commit();
                return affected;
            }
            catch (Exception exception)
            {
                if (ownsTransaction) _database.Rollback();
                else if (_transactionOwner != null)
                {
                    _database.Rollback();
                    _transactionAborted = true;
                }
                if (exception is LiteException) throw new DbUpdateException("LiteDB could not execute the atomic bulk command.", exception);
                throw;
            }
        }
    }
}
