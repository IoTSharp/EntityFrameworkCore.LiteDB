using IoTSharp.EntityFrameworkCore.Migration;
using LiteDB;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class MigrationArchiveTests
{
    // The old 6826e3a provider writes BsonMapper.Global.ToDocument(entity), including native _id.
    // This fixture reproduces that layout without claiming to execute the former EF6 assembly.
    [Theory, InlineData(false), InlineData(true)]
    public async Task Legacy_mapper_layout_is_explicitly_mapped_and_reencoded_after_reopen(bool async)
    {
        using var target = new ProviderTestStore(false);
        var legacyPath = Path.Combine(target.DirectoryPath, "legacy.db");
        var token = Guid.NewGuid();
        using (var legacy = new LiteDatabase(legacyPath))
        {
            legacy.GetCollection<BsonDocument>("LegacyCustomers").Insert(BsonMapper.Global.ToDocument(
                new LegacyCustomer { Id = 41, Name = "Legacy", City = "北京", Token = token }));
        }
        var archive = new StringWriter();
        using (var legacy = new LiteDatabase(new ConnectionString { Filename = legacyPath, ReadOnly = true }))
            Assert.Equal(1, LiteDBDataArchive.Export(legacy, new[] { "LegacyCustomers" }, archive));
        using (var db = target.CreateContext())
        {
            var reader = new StringReader(archive.ToString());
            Assert.Equal(1, async
                ? await LiteDBDataArchive.ImportAsync(db, reader, CustomerMappings())
                : LiteDBDataArchive.Import(db, reader, CustomerMappings()));
            Assert.Empty(db.ChangeTracker.Entries());
            var generated = new Customer { Name = "Generated" };
            db.Add(generated);
            db.SaveChanges();
            Assert.True(generated.Id > 41);
        }
        target.Reopen();
        using (var read = target.CreateContext())
        {
            var actual = read.Customers.Single(c => c.Id == 41);
            Assert.Equal("Legacy", actual.Name);
            Assert.Equal("北京", actual.City);
            Assert.Equal(token, actual.Token);
        }
        using var sourceReadback = new LiteDatabase(new ConnectionString { Filename = legacyPath, ReadOnly = true });
        Assert.Equal(41, sourceReadback.GetCollection<BsonDocument>("LegacyCustomers").FindAll().Single()["_id"].AsInt32);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Conflicting_import_rolls_back_every_record_and_clears_only_import_owned_tracker(bool async)
    {
        using var target = new ProviderTestStore();
        var archive = MakeArchive(target.DirectoryPath,
            new LegacyCustomer { Id = 40, Name = "First" }, new LegacyCustomer { Id = 41, Name = "Alice" });
        using (var db = target.CreateContext())
        {
            if (async) await Assert.ThrowsAsync<DbUpdateException>(() => LiteDBDataArchive.ImportAsync(db, new StringReader(archive), CustomerMappings()));
            else Assert.Throws<DbUpdateException>(() => LiteDBDataArchive.Import(db, new StringReader(archive), CustomerMappings()));
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Null(db.Database.CurrentTransaction);
        }
        target.Reopen();
        using var read = target.CreateContext();
        Assert.Equal(3, read.Customers.Count());
        Assert.Null(read.Customers.Find(40));
    }

    [Fact]
    public void Missing_mapping_truncated_archive_and_unknown_version_fail_before_any_write()
    {
        using var target = new ProviderTestStore();
        var archive = MakeArchive(target.DirectoryPath, new LegacyCustomer { Id = 40, Name = "Imported" });
        using var db = target.CreateContext();
        Assert.Throws<InvalidDataException>(() => LiteDBDataArchive.Import(db, new StringReader(archive), new Dictionary<string, Func<BsonDocument, object>>()));
        var records = archive.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var truncated = string.Join(Environment.NewLine, records.Take(records.Length - 1));
        Assert.Throws<InvalidDataException>(() => LiteDBDataArchive.Import(db, new StringReader(truncated), CustomerMappings()));
        var wrongCount = records.ToArray();
        wrongCount[^1] = JsonSerializer.Serialize(new BsonDocument { ["completed"] = true, ["documents"] = 99 });
        Assert.Throws<InvalidDataException>(() => LiteDBDataArchive.Import(db, new StringReader(string.Join(Environment.NewLine, wrongCount)), CustomerMappings()));
        Assert.Throws<InvalidDataException>(() => LiteDBDataArchive.Import(db, new StringReader(archive + "{}"), CustomerMappings()));
        var header = JsonSerializer.Deserialize(records[0]).AsDocument;
        header["version"] = 999;
        records[0] = JsonSerializer.Serialize(header);
        Assert.Throws<InvalidDataException>(() => LiteDBDataArchive.Import(db, new StringReader(string.Join(Environment.NewLine, records)), CustomerMappings()));
        Assert.Equal(3, db.Customers.Count());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Import_limits_cancellation_and_existing_context_state_are_preserved()
    {
        using var target = new ProviderTestStore();
        var archive = MakeArchive(target.DirectoryPath, new LegacyCustomer { Id = 40, Name = "First" }, new LegacyCustomer { Id = 41, Name = "Second" });
        using var db = target.CreateContext();
        Assert.Throws<InvalidDataException>(() => LiteDBDataArchive.Import(db, new StringReader(archive), CustomerMappings(), maximumDocuments: 1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LiteDBDataArchive.ImportAsync(db, new StringReader(archive), CustomerMappings(), cancellationToken: canceled.Token));
        using (var transaction = db.Database.BeginTransaction())
        {
            Assert.Throws<InvalidOperationException>(() => LiteDBDataArchive.Import(db, new StringReader(archive), CustomerMappings()));
            Assert.Same(transaction, db.Database.CurrentTransaction);
            transaction.Rollback();
        }
        var tracked = db.Customers.Single(c => c.Id == 1);
        tracked.City = "Pending";
        Assert.Throws<InvalidOperationException>(() => LiteDBDataArchive.Import(db, new StringReader(archive), CustomerMappings()));
        Assert.Same(tracked, Assert.Single(db.ChangeTracker.Entries()).Entity);
        Assert.Equal("Pending", tracked.City);
        Assert.Equal(3, db.Customers.Count());
    }

    [Fact]
    public void Extended_BSON_types_survive_archive_and_selected_collections_are_required()
    {
        using var target = new ProviderTestStore(false);
        using var source = new LiteDatabase(Path.Combine(target.DirectoryPath, "source.db"));
        var id = ObjectId.NewObjectId();
        var original = new BsonDocument
        {
            ["_id"] = id, ["bytes"] = new byte[] { 0, 255 }, ["decimal"] = 123.4567890123456789012345678m,
            ["guid"] = Guid.NewGuid(), ["nested"] = new BsonDocument { ["list"] = new BsonArray(1, "text", BsonValue.Null) }
        };
        source.GetCollection<BsonDocument>("Explicit").Insert(original);
        var writer = new StringWriter();
        Assert.Throws<ArgumentException>(() => LiteDBDataArchive.Export(source, new[] { "Missing" }, writer));
        Assert.Equal("", writer.ToString());
        Assert.Equal(1, LiteDBDataArchive.Export(source, new[] { "Explicit" }, writer));
        var body = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[1];
        var actual = JsonSerializer.Deserialize(body).AsDocument["document"].AsDocument;
        Assert.Equal(original, actual);
    }

    private static Dictionary<string, Func<BsonDocument, object>> CustomerMappings() => new()
    {
        ["LegacyCustomers"] = document => new Customer
        {
            Id = document["_id"].AsInt32, Name = document["Name"].AsString,
            City = document["City"].IsNull ? null : document["City"].AsString,
            Token = document["Token"].AsGuid
        }
    };

    private static string MakeArchive(string directory, params LegacyCustomer[] customers)
    {
        using var legacy = new LiteDatabase(Path.Combine(directory, "fixture-" + Guid.NewGuid().ToString("N") + ".db"));
        foreach (var customer in customers)
            legacy.GetCollection<BsonDocument>("LegacyCustomers").Insert(BsonMapper.Global.ToDocument(customer));
        var writer = new StringWriter();
        LiteDBDataArchive.Export(legacy, new[] { "LegacyCustomers" }, writer);
        return writer.ToString();
    }

    private sealed class LegacyCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? City { get; set; }
        public Guid Token { get; set; }
    }
}
