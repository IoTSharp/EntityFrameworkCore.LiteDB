using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed partial class LiteDBTable
{
    private readonly ILiteCollection<BsonDocument> _documents;
    private readonly IProperty[] _properties;
    private readonly IKey _primaryKey;
    private readonly IIndex[] _indexes;
    private readonly IKey[] _alternateKeys;
    private readonly Dictionary<IProperty, object> _generators = new();

    public LiteDBTable(LiteDatabase database, IEntityType entityType)
    {
        _properties = entityType.GetLiteDBProperties().OrderBy(p => p.GetIndex()).ToArray();
        _primaryKey = entityType.FindPrimaryKey();
        _alternateKeys = entityType.GetKeys().Where(key => key != _primaryKey).ToArray();
        _indexes = entityType.GetIndexes().ToArray();
        _documents = database.GetCollection<BsonDocument>(CollectionName(entityType));
        // Materialize the collection, including an empty collection created by EnsureCreated.
        _documents.EnsureIndex("ef_document", "$._id");
        for (var i = 0; i < _indexes.Length; i++)
        {
            _documents.EnsureIndex($"idx{i}", $"$.__index{i}", _indexes[i].IsUnique);
            if (_indexes[i].Properties.Count == 1)
                _documents.EnsureIndex($"query{i}", "$." + _indexes[i].Properties[0].Name);
        }
        for (var i = 0; i < _alternateKeys.Length; i++)
            _documents.EnsureIndex($"alternate{i}", $"$.__key{i}", unique: true);
    }

    public static string CollectionName(IEntityType entityType)
    {
        var name = "ef_" + entityType.Name.Replace('.', '_').Replace('+', '_');
        if (name.Length <= 100 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return name;
        return "ef_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entityType.Name)));
    }

    public long Maximum(IProperty property)
        => _documents.FindAll().Select(doc => LiteDBValueSerialization.Read(property, doc.ReadLiteDBValue(property)))
            .Where(value => value != null).Select(value => Convert.ToInt64(value)).DefaultIfEmpty(0).Max();

    public IReadOnlyList<object[]> SnapshotRows(CancellationToken cancellationToken = default)
        => ReadRows(_documents.FindAll(), cancellationToken);

    public IReadOnlyList<object[]> ExecuteQuery(IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.LiteDBQueryPlan plan, object[] values, CancellationToken cancellationToken)
    {
        if (plan.TakeParameter is int take && Convert.ToInt32(values[take]) <= 0) return [];
        return ReadRows(BuildQuery(plan, values).ToEnumerable(), cancellationToken);
    }

    public object ExecuteScalar(IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.LiteDBQueryPlan plan, object[] values, string operation)
    {
        if (plan.AggregateProperty != null)
            return IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.Native.LiteDBNativeAggregate.Execute(_documents, plan, values, operation);
        var query = BuildQuery(plan, values);
        return operation switch
        {
            nameof(Enumerable.Count) => query.Count(),
            nameof(Enumerable.LongCount) => query.LongCount(),
            nameof(Enumerable.Any) => query.Exists(),
            _ => throw new NotSupportedException($"Unsupported LiteDB aggregate '{operation}'.")
        };
    }

    private ILiteQueryableResult<BsonDocument> BuildQuery(IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.LiteDBQueryPlan plan, object[] values)
    {
        var parameters = new BsonDocument();
        for (var i = 0; i < values.Length; i++)
            parameters[$"p{i}"] = plan.CreateParameter(i, values[i]);
        var query = _documents.Query();
        if (plan.Predicate != null) query = query.Where(plan.Predicate, parameters);
        if (plan.OrderBy != null) query = query.OrderBy(plan.OrderBy, plan.Descending ? -1 : 1);
        ILiteQueryableResult<BsonDocument> result = query;
        if (plan.SkipParameter is int skip) result = result.Skip(Math.Max(0, Convert.ToInt32(values[skip])));
        if (plan.TakeParameter is int take)
        {
            var count = Convert.ToInt32(values[take]);
            result = result.Limit(Math.Max(0, count));
        }
        return result;
    }

    private IReadOnlyList<object[]> ReadRows(IEnumerable<BsonDocument> documents, CancellationToken cancellationToken)
    {
        var rows = new List<object[]>();
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new object[_properties.Length];
            foreach (var property in _properties)
            {
                values[property.GetIndex()] = LiteDBValueSerialization.Read(property, document.ReadLiteDBValue(property));
            }
            rows.Add(values);
        }
        return rows;
    }

    public void Create(IUpdateEntry entry)
    {
        var document = CreateDocument(entry);
        _documents.Insert(document);
        BumpGenerators(entry);
    }

    public void Delete(IUpdateEntry entry)
    {
        var id = CreateId(entry);
        CheckConcurrency(entry, _documents.FindById(id));
        if (!_documents.Delete(id)) throw ConcurrencyException(entry);
    }

    public void Update(IUpdateEntry entry)
    {
        var id = CreateId(entry);
        var document = _documents.FindById(id);
        CheckConcurrency(entry, document);
        // Preserve values of properties not marked modified, including detached partial updates.
        foreach (var property in _properties.Where(entry.IsModified))
        {
            WriteProperty(entry, property, document);
        }
        LiteDBComplexPresence.Apply(entry, document, _properties);
        LiteDBGeneratedValues.Apply(entry, document, _properties);
        WriteIndexes(document);
        if (!_documents.Update(document)) throw ConcurrencyException(entry);
        BumpGenerators(entry);
    }

    public LiteDBIntegerValueGenerator<TProperty> GetIntegerValueGenerator<TProperty>(IProperty property)
    {
        var maximum = Maximum(property);
        if (!_generators.TryGetValue(property, out var existing))
        {
            existing = new LiteDBIntegerValueGenerator<TProperty>(maximum);
            _generators.Add(property, existing);
        }
        var generator = (LiteDBIntegerValueGenerator<TProperty>)existing;
        generator.Bump(maximum);
        return generator;
    }

    private void BumpGenerators(IUpdateEntry entry)
    {
        foreach (var (property, generator) in _generators)
        {
            generator.GetType().GetMethod("Bump")!.Invoke(generator, [Convert.ToInt64(entry.GetCurrentValue(property))]);
        }
    }

    private BsonDocument CreateDocument(IUpdateEntry entry)
    {
        var document = new BsonDocument { ["_id"] = CreateId(entry) };
        foreach (var property in _properties) WriteProperty(entry, property, document);
        LiteDBComplexPresence.Apply(entry, document, _properties);
        LiteDBGeneratedValues.Apply(entry, document, _properties);
        WriteIndexes(document);
        return document;
    }

    private static void WriteProperty(IUpdateEntry entry, IProperty property, BsonDocument document)
    {
        if (LiteDBGeneratedValues.ShouldGenerate(entry, property)) return;
        var value = entry.GetCurrentValue(property);
        if (!property.IsNullable && value == null)
        {
            throw new DbUpdateException($"Required property '{property.DeclaringType.DisplayName()}.{property.Name}' is null.", [entry]);
        }
        document.WriteLiteDBValue(property, LiteDBValueSerialization.Write(property, value));
    }

    private void WriteIndexes(BsonDocument document)
    {
        for (var i = 0; i < _alternateKeys.Length; i++)
            document[$"__key{i}"] = LiteDBValueSerialization.Key(_alternateKeys[i].Properties.Select(p => document[p.Name]));
        for (var i = 0; i < _indexes.Length; i++)
        {
            document[$"__index{i}"] = LiteDBValueSerialization.Key(_indexes[i].Properties.Select(p => document[p.Name]));
        }
    }

    private BsonValue CreateId(IUpdateEntry entry)
        => _primaryKey == null ? ObjectId.NewObjectId() : LiteDBValueSerialization.Key(
            _primaryKey.Properties.Select(p => LiteDBValueSerialization.Write(p, entry.GetCurrentValue(p))));

    private void CheckConcurrency(IUpdateEntry entry, BsonDocument document)
    {
        if (document == null) throw ConcurrencyException(entry);
        foreach (var property in _properties.Where(p => p.IsConcurrencyToken))
        {
            var persisted = LiteDBValueSerialization.Read(property, document.ReadLiteDBValue(property));
            if (!property.GetValueComparer().Equals(persisted, entry.GetOriginalValue(property))) throw ConcurrencyException(entry);
        }
    }

    private static DbUpdateConcurrencyException ConcurrencyException(IUpdateEntry entry)
        => new($"The LiteDB document for '{entry.EntityType.DisplayName()}' was changed or deleted.", [entry]);
}
