using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class BulkGeneratedValueTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Bulk_recomputes_generated_values_without_refreshing_tracker_or_rerunning_defaults(bool async)
    {
        using var file = new ProviderTestStore(false);
        using (var root = new LiteDBDatabaseRoot())
        using (var db = CreateContext(file.FilePath, root))
        {
            var invoice = new Invoice { Id = 1, Name = "Original", Quantity = 2, UnitPrice = 3.5m };
            db.Add(invoice);
            db.SaveChanges();
            Assert.Equal(7m, invoice.Total);
            Assert.Equal("ORIGINAL", invoice.Slug);
            Assert.Equal(1, async
                ? await db.Invoices.ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, i => i.Quantity + 2).SetProperty(i => i.Name, "Changed"))
                : db.Invoices.ExecuteUpdate(s => s.SetProperty(i => i.Quantity, i => i.Quantity + 2).SetProperty(i => i.Name, "Changed")));
            Assert.Equal(2, invoice.Quantity);
            Assert.Equal(7m, invoice.Total);
            Assert.Equal("Original", invoice.Name);
            Assert.Equal(EntityState.Unchanged, db.Entry(invoice).State);
            Assert.Throws<InvalidOperationException>(() => db.Invoices.ExecuteUpdate(s => s.SetProperty(i => i.Total, 99m)));
        }
        using var reopened = new LiteDBDatabaseRoot();
        using var read = CreateContext(file.FilePath, reopened);
        var result = read.Invoices.Single();
        Assert.Equal(4, result.Quantity);
        Assert.Equal(14m, result.Total);
        Assert.Equal("Changed", result.Name);
        Assert.Equal("ORIGINAL", result.Slug);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Computed_unique_index_failure_rolls_back_all_bulk_changes(bool async)
    {
        using var file = new ProviderTestStore(false);
        using (var root = new LiteDBDatabaseRoot())
        using (var db = CreateContext(file.FilePath, root))
        {
            db.AddRange(new Invoice { Id = 1, Name = "First", Quantity = 1, UnitPrice = 2m },
                new Invoice { Id = 2, Name = "Second", Quantity = 2, UnitPrice = 2m });
            db.SaveChanges();
            if (async) await Assert.ThrowsAsync<DbUpdateException>(() => db.Invoices.ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, 5)));
            else Assert.Throws<DbUpdateException>(() => db.Invoices.ExecuteUpdate(s => s.SetProperty(i => i.Quantity, 5)));
        }
        using var reopened = new LiteDBDatabaseRoot();
        using var read = CreateContext(file.FilePath, reopened);
        Assert.Equal(new[] { 1, 2 }, read.Invoices.OrderBy(i => i.Id).Select(i => i.Quantity).ToArray());
        Assert.Equal(new[] { 2m, 4m }, read.Invoices.OrderBy(i => i.Id).Select(i => i.Total).ToArray());
    }

    private static InvoiceContext CreateContext(string path, LiteDBDatabaseRoot root)
        => new(new DbContextOptionsBuilder<InvoiceContext>().UseLiteDB(path, root).Options);

    private sealed class InvoiceContext(DbContextOptions<InvoiceContext> options) : DbContext(options)
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Invoice>().Property(i => i.Slug).HasLiteDBDefaultExpression("UPPER($.Name)");
            model.Entity<Invoice>().Property(i => i.Total).HasLiteDBComputedExpression("$.Quantity * $.UnitPrice");
            model.Entity<Invoice>().HasIndex(i => i.Total).IsUnique();
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
}
