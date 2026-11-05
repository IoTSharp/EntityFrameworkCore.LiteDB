using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

internal sealed class ProviderTestStore : IDisposable
{
    private LiteDBDatabaseRoot _root = new();
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "iotsharp-litedb-" + Guid.NewGuid().ToString("N"));
    public string FilePath => Path.Combine(DirectoryPath, "tests.db");

    public ProviderTestStore(bool seed = true)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!seed) return;
        using var db = CreateContext();
        db.Customers.AddRange(
            new Customer { Id = 1, Name = "Alice", City = "London", Rating = 4 },
            new Customer { Id = 2, Name = "Bob", City = "Seattle", Rating = null },
            new Customer { Id = 3, Name = "Carol", City = null, Rating = 2 });
        db.Orders.AddRange(
            new Order { Id = 1, CustomerId = 1, Amount = 10 },
            new Order { Id = 2, CustomerId = 1, Amount = 20 },
            new Order { Id = 3, CustomerId = 2, Amount = 30 });
        db.SaveChanges();
    }

    public ProviderContext CreateContext(Action<DbContextOptionsBuilder>? configure = null) => new(FilePath, _root, configure);

    public void Reopen()
    {
        _root.Dispose();
        _root = new();
    }

    public void Dispose()
    {
        _root.Dispose();
        Directory.Delete(DirectoryPath, recursive: true);
    }
}

internal sealed class ProviderContext(string path, LiteDBDatabaseRoot root, Action<DbContextOptionsBuilder>? configure = null) : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<CompositeEntity> Composites => Set<CompositeEntity>();
    public DbSet<ScalarEntity> Scalars => Set<ScalarEntity>();
    public DbSet<Animal> Animals => Set<Animal>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseLiteDB(path, root);
        configure?.Invoke(options);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Customer>().Property(c => c.Name).IsRequired();
        model.Entity<Customer>().Property(c => c.Token).IsConcurrencyToken();
        model.Entity<Customer>().Property<string>("ShadowTag");
        model.Entity<Customer>().HasIndex(c => c.Name).IsUnique();
        model.Entity<Customer>().HasMany(c => c.Orders).WithOne(o => o.Customer).HasForeignKey(o => o.CustomerId);
        model.Entity<CompositeEntity>().HasKey(e => new { e.Left, e.Right });
        model.Entity<ScalarEntity>().Property(e => e.Wrapped).HasConversion(value => value.Value, value => new WrappedString(value));
        model.Entity<Cat>();
        model.Entity<Dog>();
        model.Entity<Owner>().OwnsOne(o => o.Address);
        model.Entity<Owner>().HasMany(o => o.Tags).WithMany(t => t.Owners);
    }
}

internal sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? City { get; set; }
    public int? Rating { get; set; }
    public Guid Token { get; set; }
    public List<Order> Orders { get; set; } = [];
}

internal sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Amount { get; set; }
}

internal sealed class CompositeEntity
{
    public string Left { get; set; } = "";
    public string Right { get; set; } = "";
    public string Value { get; set; } = "";
}

internal sealed class ScalarEntity
{
    public int Id { get; set; }
    public bool Boolean { get; set; }
    public byte Byte { get; set; }
    public sbyte SignedByte { get; set; }
    public short Short { get; set; }
    public ushort UnsignedShort { get; set; }
    public int Integer { get; set; }
    public uint UnsignedInteger { get; set; }
    public long Long { get; set; }
    public ulong UnsignedLong { get; set; }
    public float Single { get; set; }
    public double Double { get; set; }
    public decimal Decimal { get; set; }
    public char Character { get; set; }
    public string? Text { get; set; }
    public byte[] Bytes { get; set; } = [];
    public Guid Guid { get; set; }
    public DateTime DateTime { get; set; }
    public DateTimeOffset Offset { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public TimeSpan Duration { get; set; }
    public DayOfWeek Enum { get; set; }
    public int? Nullable { get; set; }
    public WrappedString Wrapped { get; set; } = new("");
    public List<int> Integers { get; set; } = [];
    public string[] Words { get; set; } = [];
}

internal readonly record struct WrappedString(string Value);

internal abstract class Animal { public int Id { get; set; } public string Name { get; set; } = ""; }
internal sealed class Cat : Animal { public int Lives { get; set; } }
internal sealed class Dog : Animal { public bool Trained { get; set; } }

internal sealed class Owner
{
    public int Id { get; set; }
    public Address Address { get; set; } = new();
    public List<Tag> Tags { get; set; } = [];
}
internal sealed class Address { public string Street { get; set; } = ""; public string? ZipCode { get; set; } }
internal sealed class Tag { public int Id { get; set; } public string Name { get; set; } = ""; public List<Owner> Owners { get; set; } = []; }
