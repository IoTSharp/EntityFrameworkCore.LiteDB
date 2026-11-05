using LiteDB;
using System.IO;

namespace IoTSharp.EntityFrameworkCore.Migration;

/// <summary>
/// Exports explicitly selected BSON collections and imports them through explicit entity mappings.
/// This is a copy-and-reencode migration path, not an in-place file or binary compatibility promise.
/// </summary>
public static class LiteDBDataArchive
{
    private const string Format = "iotsharp-litedb-archive";
    private const int Version = 1;

    /// <summary>
    /// Writes a versioned extended-JSON line archive with a completion record. The caller owns the
    /// database and writer; open a backed-up legacy database read-only and select its collection names explicitly.
    /// </summary>
    public static int Export(LiteDatabase source, IReadOnlyCollection<string> collectionNames, TextWriter destination,
        int maximumDocuments = 100_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(collectionNames);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDocuments);
        var names = collectionNames.ToArray();
        if (names.Length == 0 || names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new ArgumentException("Select at least one distinct, nonempty collection name.", nameof(collectionNames));
        var existing = source.GetCollectionNames().ToHashSet(StringComparer.Ordinal);
        if (names.Any(name => !existing.Contains(name)))
            throw new ArgumentException("An explicitly selected collection does not exist in the source database.", nameof(collectionNames));
        cancellationToken.ThrowIfCancellationRequested();
        void WriteRecord(BsonDocument record)
        {
            var line = JsonSerializer.Serialize(record);
            if (line.Length > 16 * 1024 * 1024) throw new InvalidDataException("An archive line exceeds 16 MiB.");
            destination.WriteLine(line);
        }
        WriteRecord(new BsonDocument
        {
            ["format"] = Format, ["version"] = Version,
            ["collections"] = new BsonArray(names.Select(name => new BsonValue(name)))
        });
        var count = 0;
        foreach (var name in names)
        foreach (var document in source.GetCollection<BsonDocument>(name).FindAll())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count == maximumDocuments) throw new InvalidDataException("The archive exceeds the configured document limit.");
            WriteRecord(new BsonDocument { ["collection"] = name, ["document"] = document });
            count++;
        }
        WriteRecord(new BsonDocument { ["completed"] = true, ["documents"] = count });
        destination.Flush();
        return count;
    }

    /// <summary>
    /// Validates a complete archive, maps each document to a new model entity, and atomically inserts all
    /// mapped entities. Requires an empty tracker and no existing transaction; clears only import-owned
    /// tracked entries on both success and failure. Existing records are never silently overwritten.
    /// </summary>
    public static int Import(DbContext context, TextReader source,
        IReadOnlyDictionary<string, Func<BsonDocument, object>> mappings,
        int maximumDocuments = 100_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDocuments);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.ProviderName != "IoTSharp.EntityFrameworkCore.LiteDB")
            throw new ArgumentException("The target context must use IoTSharp.EntityFrameworkCore.LiteDB.", nameof(context));
        if (context.ChangeTracker.Entries().Any() || context.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Import requires an empty ChangeTracker and no existing transaction.");
        var map = mappings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var documents = ReadArchive(source, map, maximumDocuments, cancellationToken);
        using var transaction = context.Database.BeginTransaction();
        try
        {
            foreach (var (collection, document) in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entity = map[collection](document) ?? throw new InvalidDataException($"Mapping '{collection}' returned null.");
                context.Add(entity);
            }
            context.SaveChanges();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return documents.Count;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Cancellation-aware async API for import. LiteDB operations execute synchronously on the calling
    /// thread so its thread-affine local transaction does not migrate across awaits.
    /// </summary>
    public static Task<int> ImportAsync(DbContext context, TextReader source,
        IReadOnlyDictionary<string, Func<BsonDocument, object>> mappings,
        int maximumDocuments = 100_000, CancellationToken cancellationToken = default)
        => Task.FromResult(Import(context, source, mappings, maximumDocuments, cancellationToken));

    private static List<(string Collection, BsonDocument Document)> ReadArchive(TextReader source,
        Dictionary<string, Func<BsonDocument, object>> mappings, int maximumDocuments, CancellationToken cancellationToken)
    {
        BsonDocument ReadDocument(string? line)
        {
            if (line == null) throw new InvalidDataException("The archive is incomplete; its completion record is missing.");
            if (line.Length > 16 * 1024 * 1024) throw new InvalidDataException("An archive line exceeds 16 MiB.");
            try
            {
                var value = JsonSerializer.Deserialize(line);
                return value.IsDocument ? value.AsDocument : throw new InvalidDataException("An archive line is not a BSON document.");
            }
            catch (Exception exception) when (exception is not InvalidDataException)
            {
                throw new InvalidDataException("An archive line is not valid extended JSON.", exception);
            }
        }
        var header = ReadDocument(source.ReadLine());
        if (!header["format"].IsString || header["format"].AsString != Format || !header["version"].IsInt32
            || header["version"].AsInt32 != Version || !header["collections"].IsArray)
            throw new InvalidDataException("The archive format/version is unsupported.");
        var names = header["collections"].AsArray;
        if (names.Count == 0 || names.Any(name => !name.IsString || string.IsNullOrWhiteSpace(name.AsString)))
            throw new InvalidDataException("The archive has no valid collection selection.");
        var selected = names.Select(name => name.AsString).ToHashSet(StringComparer.Ordinal);
        if (selected.Count != names.Count || selected.Any(name => !mappings.TryGetValue(name, out var mapping) || mapping == null))
            throw new InvalidDataException("Every selected source collection requires an explicit, non-null mapping.");
        var result = new List<(string, BsonDocument)>();
        // Bound by the document limit plus one mandatory completion record.
        for (var i = 0; i <= maximumDocuments; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = ReadDocument(source.ReadLine());
            if (record["completed"].IsBoolean && record["completed"].AsBoolean)
            {
                if (!record["documents"].IsInt32 || record["documents"].AsInt32 != result.Count || source.ReadLine() != null)
                    throw new InvalidDataException("The archive completion count or trailing content is invalid.");
                return result;
            }
            if (i == maximumDocuments) throw new InvalidDataException("The archive exceeds the configured document limit.");
            if (!record["collection"].IsString || !selected.Contains(record["collection"].AsString) || !record["document"].IsDocument)
                throw new InvalidDataException("The archive record does not match its collection selection.");
            result.Add((record["collection"].AsString, record["document"].AsDocument));
        }
        throw new InvalidDataException("The archive is incomplete.");
    }
}
