using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class BulkOptionalComplexPropertyTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Setting_optional_complex_leaves_creates_only_their_ancestors_and_keeps_tracker_unchanged(bool async)
    {
        using var file = new ProviderTestStore(false);
        using (var root = new LiteDBDatabaseRoot())
        using (var db = CreateContext(file.FilePath, root))
        {
            var tracked = new Resident { Id = 1, Billing = new AddressValue { City = "Billing" } };
            db.AddRange(tracked, new Resident { Id = 2 });
            db.SaveChanges();
            Assert.Null(tracked.Address);
            var first = db.Residents.Where(r => r.Id == 1);
            Assert.Equal(1, async
                ? await first.ExecuteUpdateAsync(s => s.SetProperty(r => r.Address!.City, "NewCity"))
                : first.ExecuteUpdate(s => s.SetProperty(r => r.Address!.City, "NewCity")));
            var afterCity = db.Residents.AsNoTracking().Single(r => r.Id == 1);
            Assert.NotNull(afterCity.Address);
            Assert.Equal("NewCity", afterCity.Address.City);
            Assert.Null(afterCity.Address.Contact);
            Assert.Null(afterCity.Address.BackupContact);
            Assert.Equal("Billing", afterCity.Billing!.City);
            Assert.Null(tracked.Address);

            // Row 1 already has Address; row 2 still needs both Address and Contact presence.
            Assert.Equal(2, async
                ? await db.Residents.ExecuteUpdateAsync(s => s.SetProperty(r => r.Address!.Contact!.Email, "mail@example.test"))
                : db.Residents.ExecuteUpdate(s => s.SetProperty(r => r.Address!.Contact!.Email, "mail@example.test")));
            Assert.Null(tracked.Address);
            Assert.Equal("Billing", tracked.Billing!.City);
            Assert.All(db.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        }
        using var reopened = new LiteDBDatabaseRoot();
        using var read = CreateContext(file.FilePath, reopened);
        var results = read.Residents.OrderBy(r => r.Id).ToArray();
        Assert.Equal("NewCity", results[0].Address!.City);
        Assert.Null(results[1].Address!.City);
        Assert.All(results, resident =>
        {
            Assert.NotNull(resident.Address);
            Assert.NotNull(resident.Address.Contact);
            Assert.Equal("mail@example.test", resident.Address.Contact.Email);
            Assert.Null(resident.Address.BackupContact);
        });
        Assert.Equal("Billing", results[0].Billing!.City);
        Assert.Null(results[1].Billing);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Setting_a_nullable_leaf_to_null_creates_a_present_default_valued_complex_object(bool async)
    {
        using var file = new ProviderTestStore(false);
        using (var root = new LiteDBDatabaseRoot())
        using (var db = CreateContext(file.FilePath, root))
        {
            var tracked = new Resident { Id = 1 };
            db.Add(tracked);
            db.SaveChanges();
            Assert.Equal(1, async
                ? await db.Residents.ExecuteUpdateAsync(s => s.SetProperty(r => r.Address!.City, (string?)null))
                : db.Residents.ExecuteUpdate(s => s.SetProperty(r => r.Address!.City, (string?)null)));
            Assert.Null(tracked.Address);
            Assert.Equal(EntityState.Unchanged, db.Entry(tracked).State);
        }
        using var reopened = new LiteDBDatabaseRoot();
        using var read = CreateContext(file.FilePath, reopened);
        var result = read.Residents.Single();
        Assert.NotNull(result.Address);
        Assert.Null(result.Address.City);
        Assert.Null(result.Address.Contact);
        Assert.Null(result.Address.BackupContact);
        Assert.Null(result.Billing);
    }

    private static OptionalContext CreateContext(string path, LiteDBDatabaseRoot root)
        => new(new DbContextOptionsBuilder<OptionalContext>().UseLiteDB(path, root).Options);

    private sealed class OptionalContext(DbContextOptions<OptionalContext> options) : DbContext(options)
    {
        public DbSet<Resident> Residents => Set<Resident>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Resident>().ComplexProperty(r => r.Address, address =>
            {
                address.IsRequired(false);
                address.ComplexProperty(a => a.Contact).IsRequired(false);
                address.ComplexProperty(a => a.BackupContact).IsRequired(false);
            });
            model.Entity<Resident>().ComplexProperty(r => r.Billing, address =>
            {
                address.IsRequired(false);
                address.ComplexProperty(a => a.Contact).IsRequired(false);
                address.ComplexProperty(a => a.BackupContact).IsRequired(false);
            });
        }
    }
    private sealed class Resident { public int Id { get; set; } public AddressValue? Address { get; set; } public AddressValue? Billing { get; set; } }
    private sealed class AddressValue { public string? City { get; set; } public ContactValue? Contact { get; set; } public ContactValue? BackupContact { get; set; } }
    private sealed class ContactValue { public string? Email { get; set; } }
}
