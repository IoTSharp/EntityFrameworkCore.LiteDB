using IoTSharp.EntityFrameworkCore.Storage;
using LiteDB;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class ComplexPropertyTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Nested_complex_values_round_trip_query_project_and_track_leaf_changes(bool async)
    {
        using var store = new ComplexStore();
        using (var db = store.CreateContext())
        {
            db.Add(new ComplexCustomer
            {
                Name = "first",
                Address = new ComplexAddress { Street = "  Main Street  ", Note = null, Location = new GeoPoint { Latitude = 42.25m, Longitude = 84.5m } }
            });
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using (var db = store.CreateContext())
        {
            var query = db.Customers.Where(c => c.Address.Location.Latitude == 42.25m);
            var entity = async ? await query.SingleAsync() : query.Single();
            Assert.Equal("  Main Street  ", entity.Address.Street);
            Assert.Null(entity.Address.Note);
            Assert.Equal(84.5m, entity.Address.Location.Longitude);
            Assert.False(db.Entry(entity).ComplexProperty(e => e.Address).IsModified);
            entity.Address.Street = "Changed";
            entity.Address.Location.Latitude = 43.5m;
            db.ChangeTracker.DetectChanges();
            Assert.True(db.Entry(entity).ComplexProperty(e => e.Address).Property(e => e.Street).IsModified);
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
            Assert.Equal(0, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using (var db = store.CreateContext())
        {
            var projected = db.Customers.Select(c => new { c.Address.Street, c.Address.Location.Latitude, c.Address.Note });
            var value = async ? await projected.SingleAsync() : projected.Single();
            Assert.Equal("Changed", value.Street);
            Assert.Equal(43.5m, value.Latitude);
            Assert.Null(value.Note);
            var complexProjection = db.Customers.AsNoTracking().Select(c => c.Address);
            var address = async ? await complexProjection.SingleAsync() : complexProjection.Single();
            Assert.Equal("Changed", address.Street);
            Assert.Equal(43.5m, address.Location.Latitude);
        }
        store.Reopen();
        using var engine = new LiteDatabase(store.Path);
        var document = engine.GetCollection<BsonDocument>(engine.GetCollectionNames().Single(n => n.StartsWith("ef_"))).FindAll().Single();
        Assert.True(document["Address"].IsDocument);
        Assert.Equal("Changed", document["Address"]["Street"].AsString);
        Assert.Equal(43.5m, document["Address"]["Location"]["Latitude"].AsDecimal);
        Assert.False(document.ContainsKey("Street"));
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Required_complex_leaf_failure_rolls_back_whole_batch(bool async)
    {
        using var store = new ComplexStore();
        using (var db = store.CreateContext())
        {
            db.AddRange(
                new ComplexCustomer { Name = "valid", Address = new ComplexAddress { Street = "valid" } },
                new ComplexCustomer { Name = "invalid", Address = new ComplexAddress { Street = null! } });
            if (async) await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            else Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
        store.Reopen();
        using var verify = store.CreateContext();
        Assert.Empty(async ? await verify.Customers.ToListAsync() : verify.Customers.ToList());
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Optional_complex_value_round_trips_null_and_replacement(bool async)
    {
        using var store = new ComplexStore();
        using (var db = store.CreateContext())
        {
            db.Add(new OptionalCustomer());
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using (var db = store.CreateContext())
        {
            var entity = async ? await db.OptionalCustomers.SingleAsync() : db.OptionalCustomers.Single();
            Assert.Null(entity.Address);
            var nullQuery = db.OptionalCustomers.Where(c => c.Address == null);
            Assert.Equal(1, async ? await nullQuery.CountAsync() : nullQuery.Count());
            entity.Address = new OptionalAddress { City = "Urumqi", Zip = 830000 };
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using (var db = store.CreateContext())
        {
            var entity = async ? await db.OptionalCustomers.SingleAsync() : db.OptionalCustomers.Single();
            Assert.Equal("Urumqi", entity.Address!.City);
            Assert.Equal(830000, entity.Address.Zip);
            var nonNullQuery = db.OptionalCustomers.Where(c => c.Address != null);
            Assert.Equal(1, async ? await nonNullQuery.CountAsync() : nonNullQuery.Count());
            entity.Address = null;
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using var verify = store.CreateContext();
        var actual = async ? await verify.OptionalCustomers.SingleAsync() : verify.OptionalCustomers.Single();
        Assert.Null(actual.Address);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Optional_complex_presence_distinguishes_null_from_default_valued_objects(bool async)
    {
        using var store = new ComplexStore();
        using (var db = store.CreateContext())
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.Add(new OptionalCustomer { Address = new OptionalAddress { Contact = new ContactInfo() } });
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using (var db = store.CreateContext())
        {
            var query = db.OptionalCustomers.AsNoTracking().Select(c => c.Address);
            var address = async ? await query.SingleAsync() : query.Single();
            Assert.NotNull(address);
            Assert.Null(address.City);
            Assert.Equal(0, address.Zip);
            Assert.NotNull(address.Contact);
            Assert.Null(address.Contact.Email);
        }
        using (var db = store.CreateContext())
        {
            var entity = async ? await db.OptionalCustomers.SingleAsync() : db.OptionalCustomers.Single();
            entity.Address!.Contact = null;
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using (var db = store.CreateContext())
        {
            var entity = async ? await db.OptionalCustomers.SingleAsync() : db.OptionalCustomers.Single();
            Assert.NotNull(entity.Address);
            Assert.Null(entity.Address.Contact);
            entity.Address = null;
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using var verify = store.CreateContext();
        var finalQuery = verify.OptionalCustomers.AsNoTracking().Select(c => c.Address);
        Assert.Null(async ? await finalQuery.SingleAsync() : finalQuery.Single());
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task ToJson_container_name_maps_to_nested_BSON_and_reopens(bool async)
    {
        using var store = new ComplexStore();
        using (var db = new JsonContext(new DbContextOptionsBuilder<JsonContext>().UseLiteDB(store.Path, store.Root).Options))
        {
            db.Add(new JsonCustomer { Address = new OptionalAddress { City = "Urumqi", Zip = 830000 } });
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using (var db = new JsonContext(new DbContextOptionsBuilder<JsonContext>().UseLiteDB(store.Path, store.Root).Options))
        {
            var query = db.Set<JsonCustomer>().Where(c => c.Address.City == "Urumqi");
            var entity = async ? await query.SingleAsync() : query.Single();
            Assert.Equal(830000, entity.Address.Zip);
            entity.Address.City = "Changed";
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using var engine = new LiteDatabase(store.Path);
        var document = engine.GetCollection<BsonDocument>(engine.GetCollectionNames().Single(n => n.StartsWith("ef_"))).FindAll().Single();
        Assert.False(document.ContainsKey("Address"));
        Assert.Equal("Changed", document["details"]["City"].AsString);
    }

    [Fact]
    public void Complex_collections_report_the_remaining_provider_boundary()
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new CollectionContext(new DbContextOptionsBuilder<CollectionContext>().UseLiteDB(":memory:", root).Options);
        var exception = Assert.Throws<NotSupportedException>(() => db.Model);
        Assert.Contains("LDB-006", exception.Message);
        Assert.DoesNotContain("LiteDB cannot", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToJson_container_cannot_overwrite_an_entity_scalar_field()
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new CollisionContext(new DbContextOptionsBuilder<CollisionContext>().UseLiteDB(":memory:", root).Options);
        var exception = Assert.Throws<NotSupportedException>(() => db.Model);
        Assert.Contains("collision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToJson_container_cannot_overwrite_the_engine_primary_key()
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new ReservedContext(new DbContextOptionsBuilder<ReservedContext>().UseLiteDB(":memory:", root).Options);
        var exception = Assert.Throws<NotSupportedException>(() => db.Model);
        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ComplexStore : IDisposable
    {
        private LiteDBDatabaseRoot _root = new();
        public LiteDBDatabaseRoot Root => _root;
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "iotsharp-complex-" + Guid.NewGuid().ToString("N") + ".db");
        public ComplexContext CreateContext() => new(new DbContextOptionsBuilder<ComplexContext>().UseLiteDB(Path, _root).Options);
        public void Reopen() { _root.Dispose(); _root = new LiteDBDatabaseRoot(); }
        public void Dispose()
        {
            _root.Dispose();
            foreach (var path in new[] { Path, System.IO.Path.ChangeExtension(Path, ".log.db") })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class ComplexContext(DbContextOptions<ComplexContext> options) : DbContext(options)
    {
        public DbSet<ComplexCustomer> Customers => Set<ComplexCustomer>();
        public DbSet<OptionalCustomer> OptionalCustomers => Set<OptionalCustomer>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ComplexCustomer>().ComplexProperty(c => c.Address, address =>
            {
                address.Property(a => a.Street).IsRequired();
                address.ComplexProperty(a => a.Location);
            });
            model.Entity<OptionalCustomer>().ComplexProperty(c => c.Address, address =>
            {
                address.IsRequired(false);
                address.ComplexProperty(a => a.Contact).IsRequired(false);
            });
        }
    }

    private sealed class ComplexCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public ComplexAddress Address { get; set; } = new();
    }
    private sealed class ComplexAddress
    {
        public string Street { get; set; } = "";
        public string? Note { get; set; }
        public GeoPoint Location { get; set; } = new();
    }
    private sealed class GeoPoint
    {
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
    }
    private sealed class OptionalCustomer
    {
        public int Id { get; set; }
        public OptionalAddress? Address { get; set; }
    }
    private sealed class OptionalAddress
    {
        public string? City { get; set; }
        public int Zip { get; set; }
        public ContactInfo? Contact { get; set; }
    }
    private sealed class ContactInfo { public string? Email { get; set; } }
    private sealed class JsonCustomer { public int Id { get; set; } public OptionalAddress Address { get; set; } = new(); }
    private sealed class JsonContext(DbContextOptions<JsonContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<JsonCustomer>().ComplexProperty(c => c.Address, address =>
            {
                address.ToJson("details");
                address.Ignore(a => a.Contact);
            });
        }
    }
    private sealed class CollectionCustomer { public int Id { get; set; } public List<ContactInfo> Contacts { get; set; } = []; }
    private sealed class CollectionContext(DbContextOptions<CollectionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
            => model.Entity<CollectionCustomer>().ComplexCollection(c => c.Contacts);
    }
    private sealed class CollisionContext(DbContextOptions<CollisionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
            => model.Entity<JsonCustomer>().ComplexProperty(c => c.Address, address =>
            {
                address.ToJson("Id");
                address.Ignore(a => a.Contact);
            });
    }
    private sealed class ReservedContext(DbContextOptions<ReservedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
            => model.Entity<JsonCustomer>().ComplexProperty(c => c.Address, address =>
            {
                address.ToJson("_id");
                address.Ignore(a => a.Contact);
            });
    }
}
