using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class BulkComplexPropertyTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Nested_complex_leaf_setters_keep_other_values_and_survive_reopen(bool async)
    {
        using var file = new ProviderTestStore(false);
        using (var root = new LiteDBDatabaseRoot())
        using (var db = CreateContext(file.FilePath, root))
        {
            db.Add(new Resident { Id = 1, Address = new AddressValue { City = "Old", Zip = "123", Country = new CountryValue { Name = "CN" } } });
            db.SaveChanges();
            var tracked = db.Residents.Single();
            var suffix = "!";
            var query = db.Residents.Where(r => r.Id == 1);
            Assert.Equal(1, async
                ? await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.Address.City, r => r.Address.City + suffix).SetProperty(r => r.Address.Country.Name, "NewCountry"))
                : query.ExecuteUpdate(s => s.SetProperty(r => r.Address.City, r => r.Address.City + suffix).SetProperty(r => r.Address.Country.Name, "NewCountry")));
            Assert.Equal("Old", tracked.Address.City);
            Assert.Equal("CN", tracked.Address.Country.Name);
        }
        using var reopened = new LiteDBDatabaseRoot();
        using var read = CreateContext(file.FilePath, reopened);
        var result = read.Residents.Single();
        Assert.Equal("Old!", result.Address.City);
        Assert.Equal("123", result.Address.Zip);
        Assert.Equal("NewCountry", result.Address.Country.Name);
    }

    private static ComplexBulkContext CreateContext(string path, LiteDBDatabaseRoot root)
        => new(new DbContextOptionsBuilder<ComplexBulkContext>().UseLiteDB(path, root).Options);

    private sealed class ComplexBulkContext(DbContextOptions<ComplexBulkContext> options) : DbContext(options)
    {
        public DbSet<Resident> Residents => Set<Resident>();
        protected override void OnModelCreating(ModelBuilder model)
            => model.Entity<Resident>().ComplexProperty(r => r.Address, address => address.ComplexProperty(a => a.Country));
    }
    private sealed class Resident { public int Id { get; set; } public AddressValue Address { get; set; } = new(); }
    private sealed class AddressValue { public string City { get; set; } = ""; public string Zip { get; set; } = ""; public CountryValue Country { get; set; } = new(); }
    private sealed class CountryValue { public string Name { get; set; } = ""; }
}
