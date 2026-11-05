using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class GeneratedValueTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Native_defaults_and_computed_values_update_the_entity_and_survive_reopen(bool async)
    {
        using var store = new GeneratedStore();
        using (var db = store.CreateContext())
        {
            var first = new Invoice { Name = "first", Quantity = 2, UnitPrice = 12.5m };
            var explicitDefault = new Invoice { Name = "second", Slug = "explicit", Quantity = 3, UnitPrice = 2m };
            db.AddRange(first, explicitDefault);
            Assert.Equal(2, async ? await db.SaveChangesAsync() : db.SaveChanges());
            Assert.Equal("FIRST", first.Slug);
            Assert.Equal(25m, first.Total);
            Assert.Equal("explicit", explicitDefault.Slug);
            Assert.Equal(6m, explicitDefault.Total);
            first.Quantity = 4;
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
            Assert.Equal(50m, first.Total);
            Assert.Equal(0, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using var verify = store.CreateContext();
        var entities = async ? await verify.Invoices.OrderBy(i => i.Id).ToListAsync() : verify.Invoices.OrderBy(i => i.Id).ToList();
        Assert.Equal(50m, entities[0].Total);
        Assert.Equal("FIRST", entities[0].Slug);
        Assert.Equal("explicit", entities[1].Slug);
        Assert.Equal(6m, entities[1].Total);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Failed_batch_discards_generated_sidecar_and_can_be_retried(bool async)
    {
        using var store = new GeneratedStore();
        using (var db = store.CreateContext())
        {
            var first = new Invoice { Name = "first", Quantity = 2, UnitPrice = 12.5m };
            var invalid = new Invoice { Name = null!, Quantity = 1, UnitPrice = 3m };
            db.AddRange(first, invalid);
            if (async) await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            else Assert.Throws<DbUpdateException>(() => db.SaveChanges());
            Assert.Equal(0m, first.Total);
            Assert.Null(first.Slug);
            invalid.Name = "fixed";
            Assert.Equal(2, async ? await db.SaveChangesAsync() : db.SaveChanges());
            Assert.Equal(25m, first.Total);
            Assert.Equal("FIXED", invalid.Slug);
        }
        store.Reopen();
        using var verify = store.CreateContext();
        Assert.Equal(2, async ? await verify.Invoices.CountAsync() : verify.Invoices.Count());
    }

    [Fact]
    public void ValueGeneratedNever_takes_precedence_over_native_expression_annotation()
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new NeverContext(new DbContextOptionsBuilder<NeverContext>().UseLiteDB(":memory:", root).Options);
        var entity = new NeverEntity { Value = 7 };
        db.Add(entity);
        db.SaveChanges();
        Assert.Equal(7, entity.Value);
        db.ChangeTracker.Clear();
        Assert.Equal(7, db.Set<NeverEntity>().Single().Value);
    }

    [Fact]
    public void Generated_on_generated_dependencies_fail_early()
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new DependencyContext(new DbContextOptionsBuilder<DependencyContext>().UseLiteDB(":memory:", root).Options);
        var exception = Assert.Throws<NotSupportedException>(() => db.Model);
        Assert.Contains("ordinary fields", exception.Message);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Native_expression_null_failure_rolls_back_and_discards_store_generated_values(bool async)
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new NullableResultContext(new DbContextOptionsBuilder<NullableResultContext>().UseLiteDB(":memory:", root).Options);
        var first = new RequiredResult { Amount = 5 };
        var invalid = new RequiredResult { Amount = 10, Invalid = true };
        db.AddRange(first, invalid);
        if (async) await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        else Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        Assert.Equal(0m, first.Result);
        Assert.Equal(0, async ? await db.Set<RequiredResult>().CountAsync() : db.Set<RequiredResult>().Count());
        invalid.Invalid = false;
        Assert.Equal(2, async ? await db.SaveChangesAsync() : db.SaveChanges());
        Assert.Equal(10m, first.Result);
        Assert.Equal(20m, invalid.Result);
    }

    private sealed class GeneratedStore : IDisposable
    {
        private LiteDBDatabaseRoot _root = new();
        private string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "iotsharp-generated-" + Guid.NewGuid().ToString("N") + ".db");
        public GeneratedContext CreateContext() => new(new DbContextOptionsBuilder<GeneratedContext>().UseLiteDB(Path, _root).Options);
        public void Reopen() { _root.Dispose(); _root = new LiteDBDatabaseRoot(); }
        public void Dispose()
        {
            _root.Dispose();
            foreach (var path in new[] { Path, System.IO.Path.ChangeExtension(Path, ".log.db") })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class GeneratedContext(DbContextOptions<GeneratedContext> options) : DbContext(options)
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Invoice>().Property(i => i.Name).IsRequired();
            model.Entity<Invoice>().Property(i => i.Slug).HasLiteDBDefaultExpression("UPPER($.Name)");
            model.Entity<Invoice>().Property(i => i.Total).HasLiteDBComputedExpression("$.Quantity * $.UnitPrice");
        }
    }
    private sealed class Invoice
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Slug { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total { get; private set; }
    }
    private sealed class NeverEntity { public int Id { get; set; } public int Value { get; set; } }
    private sealed class NeverContext(DbContextOptions<NeverContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
            => model.Entity<NeverEntity>().Property(e => e.Value).HasLiteDBComputedExpression("10").ValueGeneratedNever();
    }
    private sealed class DependencyEntity { public int Id { get; set; } public int First { get; set; } public int Second { get; set; } }
    private sealed class DependencyContext(DbContextOptions<DependencyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<DependencyEntity>().Property(e => e.First).HasLiteDBComputedExpression("1");
            model.Entity<DependencyEntity>().Property(e => e.Second).HasLiteDBComputedExpression("$.First + 1");
        }
    }
    private sealed class RequiredResult
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public bool Invalid { get; set; }
        public decimal Result { get; private set; }
    }
    private sealed class NullableResultContext(DbContextOptions<NullableResultContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
            => model.Entity<RequiredResult>().Property(e => e.Result).HasLiteDBComputedExpression("IIF($.Invalid, NULL, $.Amount * 2)");
    }
}
