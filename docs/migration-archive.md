# Explicit legacy BSON export and import

`LiteDBDataArchive` provides a versioned archive and an atomic import into the new provider. It does not open an old file as a new provider database or infer a collection/model mapping. Export a closed, backed-up source file, then import into a separate target file.

The former provider at commit `6826e3a` uses `BsonMapper.Global.ToDocument(entity)` in `LiteDBTable.Create`. Its document ID and embedded object layout differ from the new provider's key encoding and metadata-driven BSON layout. The migration regression fixture reproduces that BSON layout with LiteDB 5; executing the former EF Core 6 assembly is a separate, unexecuted compatibility check.

```csharp
using IoTSharp.EntityFrameworkCore.Migration;
using LiteDB;

using (var oldDatabase = new LiteDatabase(new ConnectionString
       { Filename = "legacy-backup.db", ReadOnly = true }))
using (var archive = File.CreateText("customers.jsonl"))
{
    // Use the collection names actually present in your backed-up source database.
    LiteDBDataArchive.Export(oldDatabase, new[] { "LegacyCustomers" }, archive);
}

using var target = new AppContext(); // UseLiteDB("new-target.db")
using var input = File.OpenText("customers.jsonl");
var imported = LiteDBDataArchive.Import(target, input,
    new Dictionary<string, Func<BsonDocument, object>>
    {
        ["LegacyCustomers"] = document => new Customer
        {
            Id = document["_id"].AsInt32,
            Name = document["Name"].AsString,
            City = document["City"].IsNull ? null : document["City"].AsString
        }
    });
```

Each selected source collection requires an explicit mapping, including collections with zero documents. Map foreign keys, shadow values, converters, complex values and navigation graphs according to the old application's actual schema; the archive preserves BSON values, but cannot recover precision or fields already absent from the source.

The version 1 JSONL format contains a format/version header, named collection/document records encoded with LiteDB extended JSON, and a mandatory completion record with the document count. Unknown versions, missing mappings, truncated archives, invalid completion counts, trailing records and configured size limits fail before writes. The default limit is 100,000 documents and each line is limited to 16 MiB. An export interrupted before its completion record is not importable.

Import requires an empty ChangeTracker and no existing transaction. It inserts through the new model inside one LiteDB local transaction; a mapping, uniqueness, serialization or cancellation failure rolls back the entire import. It clears import-owned tracked entries on success and failure, and never silently upserts existing entities. The returned number counts source documents, which can differ from EF entity rows when a mapping creates an owned graph. Retry only after examining the failure; importing a completed archive twice normally causes key/unique conflicts.

`ImportAsync` is cancellation-aware and wraps synchronous LiteDB operations on the calling thread. It does not imply asynchronous disk I/O or a transaction that can move across threads. Source export and target import leave ownership of the database, context, reader and writer with the caller.
