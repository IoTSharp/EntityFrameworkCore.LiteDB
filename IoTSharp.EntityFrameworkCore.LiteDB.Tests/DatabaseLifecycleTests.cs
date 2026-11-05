using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class DatabaseLifecycleTests
{
    [Fact]
    public void HasData_is_seeded_once_and_literal_defaults_are_generated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "iotsharp-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var root = new LiteDBDatabaseRoot())
            using (var db = Create(directory, root))
            {
                Assert.True(db.Database.EnsureCreated());
                Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd, db.Model.FindEntityType(typeof(Seeded))!.FindProperty(nameof(Seeded.Priority))!.ValueGenerated);
                var entity = new Seeded { Name = "New" };
                db.Add(entity);
                db.SaveChanges();
                Assert.Equal(42, entity.Priority);
                Assert.True(entity.Id > 100);
            }
            using (var root = new LiteDBDatabaseRoot())
            using (var db = Create(directory, root))
            {
                Assert.False(db.Database.EnsureCreated());
                Assert.Equal(2, db.Set<Seeded>().Count());
                Assert.Equal("Seed", db.Set<Seeded>().Find(100)!.Name);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Disposing_context_releases_an_unowned_transaction()
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            db.Database.BeginTransaction();
            db.Add(new Customer { Name = "Context disposed" });
            db.SaveChanges();
        }
        using var verify = store.CreateContext();
        Assert.Equal(3, verify.Customers.Count());
    }

    [Fact]
    public void Named_memory_store_is_shared_only_within_the_same_root()
    {
        using var first = new LiteDBDatabaseRoot();
        using var second = new LiteDBDatabaseRoot();
        using (var db = new ProviderContext(":memory:", first))
        {
            db.Add(new Customer { Name = "Memory" });
            db.SaveChanges();
        }
        using var shared = new ProviderContext(":memory:", first);
        using var isolated = new ProviderContext(":memory:", second);
        Assert.Single(shared.Customers);
        Assert.Empty(isolated.Customers);
        Assert.True(shared.Database.IsLiteDB());
        Assert.Equal("IoTSharp.EntityFrameworkCore.LiteDB", shared.Database.ProviderName);
    }

    private static SeedContext Create(string directory, LiteDBDatabaseRoot root)
        => new(new DbContextOptionsBuilder<SeedContext>().UseLiteDB(Path.Combine(directory, "lifecycle.db"), root).Options);

    [Fact]
    public void Explicit_ValueGeneratedNever_overrides_default_value_convention()
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new NeverGeneratedContext(new DbContextOptionsBuilder<NeverGeneratedContext>().UseLiteDB(":memory:", root).Options);
        var entity = new Seeded { Name = "Explicit zero" };
        db.Add(entity);
        db.SaveChanges();
        Assert.Equal(0, entity.Priority);
    }

    private sealed class NeverGeneratedContext(DbContextOptions<NeverGeneratedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
            => model.Entity<Seeded>().Property(s => s.Priority).HasDefaultValue(42).ValueGeneratedNever();
    }

    private sealed class SeedContext(DbContextOptions<SeedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Seeded>().Property(s => s.Priority).HasDefaultValue(42);
            model.Entity<Seeded>().HasData(new Seeded { Id = 100, Name = "Seed", Priority = 7 });
        }
    }
    private sealed class Seeded { public int Id { get; set; } public string Name { get; set; } = ""; public int Priority { get; set; } }
}
