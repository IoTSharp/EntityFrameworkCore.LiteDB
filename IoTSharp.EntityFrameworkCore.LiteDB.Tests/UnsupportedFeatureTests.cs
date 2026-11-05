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
    public void Bulk_execute_update_and_delete_report_unsupported()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        Assert.Throws<InvalidOperationException>(() => db.Customers.Where(c => c.Id == 1).ExecuteDelete());
        Assert.Throws<InvalidOperationException>(() => db.Customers.ExecuteUpdate(setters => setters.SetProperty(c => c.City, "Paris")));
        Assert.Equal(3, db.Customers.Count());
    }

    [Fact]
    public void Complex_properties_fail_early_with_support_reference()
    {
        using var root = new LiteDBDatabaseRoot();
        using var db = new ComplexContext(new DbContextOptionsBuilder<ComplexContext>().UseLiteDB(":memory:", root).Options);
        var exception = Assert.Throws<NotSupportedException>(() => db.Model);
        Assert.Contains("LDB-006", exception.Message);
    }

    private sealed class ComplexContext(DbContextOptions<ComplexContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) => model.Entity<ComplexEntity>().ComplexProperty(e => e.Address);
    }
    private sealed class ComplexEntity { public int Id { get; set; } public Address Address { get; set; } = new(); }
}
