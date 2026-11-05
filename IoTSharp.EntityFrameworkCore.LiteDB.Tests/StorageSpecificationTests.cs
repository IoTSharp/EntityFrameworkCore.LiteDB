// Concurrency scenarios adapted from EF Core v10.0.14 OptimisticConcurrencyTestBase (MIT).
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class StorageSpecificationTests
{
    [Fact]
    public void Reopen_reads_saved_data_and_advances_integer_keys()
    {
        using var store = new ProviderTestStore();
        store.Reopen();
        using var db = store.CreateContext();
        Assert.Equal(3, db.Customers.Count());
        var customer = new Customer { Name = "Dora" };
        db.Add(customer);
        db.SaveChanges();
        Assert.True(customer.Id > 3);
        Assert.Equal(4, db.Customers.Count());
    }

    [Fact]
    public void Explicit_integer_key_bumps_existing_generator()
    {
        using var store = new ProviderTestStore(false);
        using var db = store.CreateContext();
        db.Add(new Customer { Name = "Generated" });
        db.SaveChanges();
        db.Add(new Customer { Id = 500, Name = "Explicit" });
        db.SaveChanges();
        var third = new Customer { Name = "Next" };
        db.Add(third);
        db.SaveChanges();
        Assert.True(third.Id > 500);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Adding_the_same_entity_twice_results_in_DbUpdateException(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        db.Add(new Customer { Id = 1, Name = "Duplicate" });
        if (async) await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        else Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        using var verify = store.CreateContext();
        Assert.Equal("Alice", verify.Customers.Find(1)!.Name);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Deleting_the_same_entity_twice_results_in_DbUpdateConcurrencyException(bool async)
    {
        using var store = new ProviderTestStore();
        using var first = store.CreateContext();
        using var second = store.CreateContext();
        first.Remove(first.Customers.Single(c => c.Id == 3));
        second.Remove(second.Customers.Single(c => c.Id == 3));
        first.SaveChanges();
        var exception = async
            ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync())
            : Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
        Assert.IsType<Customer>(Assert.Single(exception.Entries).Entity);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Concurrency_tokens_detect_stale_updates_and_allow_reload(bool async)
    {
        using var store = new ProviderTestStore();
        using var first = store.CreateContext();
        using var second = store.CreateContext();
        var winner = first.Customers.Single(c => c.Id == 1);
        var loser = second.Customers.Single(c => c.Id == 1);
        winner.Name = "Winner";
        winner.Token = Guid.NewGuid();
        first.SaveChanges();
        loser.Name = "Loser";
        if (async) await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        else Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());
        if (async) await second.Entry(loser).ReloadAsync(); else second.Entry(loser).Reload();
        Assert.Equal("Winner", loser.Name);
    }

    [Fact]
    public void Composite_keys_do_not_collide_on_separators_or_case()
    {
        using var store = new ProviderTestStore(false);
        using (var db = store.CreateContext())
        {
            db.Composites.AddRange(
                new() { Left = "a|b", Right = "c", Value = "one" },
                new() { Left = "a", Right = "b|c", Value = "two" },
                new() { Left = "A", Right = "b|c", Value = "three" });
            db.SaveChanges();
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(3, read.Composites.Count());
        Assert.Equal("one", read.Composites.Find("a|b", "c")!.Value);
        Assert.Equal("two", read.Composites.Find("a", "b|c")!.Value);
        Assert.Equal("three", read.Composites.Find("A", "b|c")!.Value);
    }

    [Fact]
    public void Shadow_properties_are_persisted_without_navigations()
    {
        using var store = new ProviderTestStore(false);
        using (var db = store.CreateContext())
        {
            var row = new Customer { Name = "Shadow" };
            db.Add(row);
            db.Entry(row).Property("ShadowTag").CurrentValue = "tag";
            db.SaveChanges();
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal("tag", read.Customers.Select(c => EF.Property<string>(c, "ShadowTag")).Single());
    }

    [Fact]
    public void Partial_updates_preserve_unmodified_database_properties()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var detached = new Customer { Id = 1, City = "Paris" };
        db.Attach(detached);
        db.Entry(detached).Property(c => c.City).IsModified = true;
        db.SaveChanges();
        store.Reopen();
        using var read = store.CreateContext();
        var customer = read.Customers.Find(1)!;
        Assert.Equal("Alice", customer.Name);
        Assert.Equal("Paris", customer.City);
    }

    [Fact]
    public void SaveChanges_rolls_back_entire_batch_when_unique_index_fails()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        db.AddRange(new Customer { Id = 10, Name = "New" }, new Customer { Id = 11, Name = "Alice" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        using var verify = store.CreateContext();
        Assert.Equal(3, verify.Customers.Count());
        Assert.Null(verify.Customers.Find(10));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void Explicit_transactions_commit_and_rollback(bool commit)
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            using var transaction = db.Database.BeginTransaction();
            Assert.Same(transaction, db.Database.CurrentTransaction);
            db.Add(new Customer { Name = "Transaction" });
            db.SaveChanges();
            if (commit) transaction.Commit(); else transaction.Rollback();
            Assert.Null(db.Database.CurrentTransaction);
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(commit ? 4 : 3, read.Customers.Count());
    }

    [Fact]
    public void Disposing_uncommitted_transaction_rolls_back()
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            using var transaction = db.Database.BeginTransaction();
            db.Add(new Customer { Name = "Uncommitted" });
            db.SaveChanges();
        }
        using var verify = store.CreateContext();
        Assert.Equal(3, verify.Customers.Count());
    }

    [Fact]
    public async Task Cancellation_prevents_save_and_query()
    {
        using var store = new ProviderTestStore();
        await using var db = store.CreateContext();
        db.Add(new Customer { Name = "Canceled" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.SaveChangesAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Customers.ToListAsync(cancellation.Token));
        using var verify = store.CreateContext();
        Assert.Equal(3, verify.Customers.Count());
    }

    [Fact]
    public void Scalars_value_converters_and_precision_round_trip_after_reopen()
    {
        using var store = new ProviderTestStore(false);
        var expected = new ScalarEntity
        {
            Boolean = true, Byte = byte.MaxValue, SignedByte = sbyte.MinValue,
            Short = short.MinValue, UnsignedShort = ushort.MaxValue, Integer = int.MinValue,
            UnsignedInteger = uint.MaxValue, Long = long.MaxValue, UnsignedLong = ulong.MaxValue,
            Single = 1.25f, Double = 1.23456789, Decimal = 123456789012345678.123456789m,
            Character = '汉', Text = "  keep whitespace  ", Bytes = [0, 127, 255], Guid = Guid.NewGuid(),
            DateTime = new DateTime(638900000001234567, DateTimeKind.Utc),
            Offset = new DateTimeOffset(2026, 11, 5, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234),
            Date = new DateOnly(2026, 11, 5), Time = new TimeOnly(123456789), Duration = TimeSpan.FromTicks(-123456789),
            Enum = DayOfWeek.Friday, Nullable = null, Wrapped = new WrappedString("converted")
        };
        using (var db = store.CreateContext()) { db.Add(expected); db.SaveChanges(); }
        store.Reopen();
        using var read = store.CreateContext();
        var actual = read.Scalars.Single();
        foreach (var property in typeof(ScalarEntity).GetProperties())
        {
            Assert.Equal(property.GetValue(expected), property.GetValue(actual));
        }
    }

    [Fact]
    public void Inheritance_OfType_reads_concrete_collections()
    {
        using var store = new ProviderTestStore(false);
        using (var db = store.CreateContext())
        {
            db.AddRange(new Cat { Name = "Cat", Lives = 9 }, new Dog { Name = "Dog", Trained = true });
            db.SaveChanges();
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(2, read.Animals.Count());
        Assert.Equal(9, read.Animals.OfType<Cat>().Single().Lives);
        Assert.True(read.Animals.OfType<Dog>().Single().Trained);
    }
}
