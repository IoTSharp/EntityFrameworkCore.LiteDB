using System.Transactions;
using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class UnsupportedFeatureTests
{
    [Fact]
    public void Ambient_transactions_are_rejected()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        using var transaction = new TransactionScope();
        db.Add(new Customer { Name = "Ambient" });
        Assert.Throws<NotSupportedException>(() => db.SaveChanges());
    }

    [Fact]
    public void Savepoints_report_unsupported()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        using var transaction = db.Database.BeginTransaction();
        Assert.False(transaction.SupportsSavepoints);
        Assert.Throws<NotSupportedException>(() => transaction.CreateSavepoint("point"));
    }

    [Fact]
    public void Relational_migrations_and_raw_SQL_are_rejected()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        Assert.False(db.Database.IsRelational());
        Assert.Throws<InvalidOperationException>(() => db.Database.Migrate());
        Assert.Throws<InvalidOperationException>(() => db.Customers.FromSqlRaw("SELECT * FROM Customers").ToList());
    }

    [Fact]
    public void Bulk_execute_update_and_delete_use_native_writes()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        Assert.Equal(1, db.Customers.Where(c => c.Id == 1).ExecuteDelete());
        Assert.Equal(2, db.Customers.ExecuteUpdate(setters => setters.SetProperty(c => c.City, "Paris")));
        Assert.Equal(2, db.Customers.Count(c => c.City == "Paris"));
    }

    [Fact]
    public void Complex_properties_are_validated_and_persisted()
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new ComplexContext(new DbContextOptionsBuilder<ComplexContext>().UseLiteDB(":memory:", root).Options);
        db.Add(new ComplexEntity { Address = new Address { Street = "Urumqi" } });
        Assert.Equal(1, db.SaveChanges());
        db.ChangeTracker.Clear();
        Assert.Equal("Urumqi", db.Set<ComplexEntity>().Single().Address.Street);
    }

    private sealed class ComplexContext(DbContextOptions<ComplexContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) => model.Entity<ComplexEntity>().ComplexProperty(e => e.Address);
    }
    private sealed class ComplexEntity { public int Id { get; set; } public Address Address { get; set; } = new(); }
}
