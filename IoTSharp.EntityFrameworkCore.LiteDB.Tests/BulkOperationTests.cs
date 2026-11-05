using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class BulkOperationTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Update_uses_parameters_original_row_values_and_does_not_touch_tracked_entities(bool async)
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            var tracked = db.Customers.Single(c => c.Id == 1);
            var threshold = 1;
            var increment = 3;
            var city = "Paris' @p0";
            var query = db.Customers.Where(c => c.Id <= threshold);
            var affected = async
                ? await query.ExecuteUpdateAsync(s => s.SetProperty(c => c.Rating, c => c.Rating + increment).SetProperty(c => c.City, city))
                : query.ExecuteUpdate(s => s.SetProperty(c => c.Rating, c => c.Rating + increment).SetProperty(c => c.City, city));
            Assert.Equal(1, affected);
            Assert.Equal(4, tracked.Rating);
            Assert.Equal("London", tracked.City);
            Assert.Equal(EntityState.Unchanged, db.Entry(tracked).State);
            Assert.Single(db.ChangeTracker.Entries());
            threshold = 3;
            increment = 1;
            city = "Second";
            Assert.Equal(3, async
                ? await query.ExecuteUpdateAsync(s => s.SetProperty(c => c.Rating, c => c.Rating + increment).SetProperty(c => c.City, city))
                : query.ExecuteUpdate(s => s.SetProperty(c => c.Rating, c => c.Rating + increment).SetProperty(c => c.City, city)));
            Assert.Equal(3, async
                ? await query.ExecuteUpdateAsync(s => s.SetProperty(c => c.City, city))
                : query.ExecuteUpdate(s => s.SetProperty(c => c.City, city))); // Matched rows include unchanged values.
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(new int?[] { 8, null, 3 }, read.Customers.OrderBy(c => c.Id).Select(c => c.Rating).ToArray());
        Assert.All(read.Customers, c => Assert.Equal("Second", c.City));
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Delete_counts_matches_preserves_tracker_and_persists_after_reopen(bool async)
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            var tracked = db.Customers.Single(c => c.Id == 1);
            var cutoff = 2;
            var query = db.Customers.Where(c => c.Id <= cutoff);
            Assert.Equal(2, async ? await query.ExecuteDeleteAsync() : query.ExecuteDelete());
            Assert.Equal(EntityState.Unchanged, db.Entry(tracked).State);
            Assert.Single(db.ChangeTracker.Entries());
            Assert.Equal(0, async ? await query.ExecuteDeleteAsync() : query.ExecuteDelete());
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(3, read.Customers.Single().Id);
        Assert.Equal(3, read.Orders.Count()); // No database-side foreign-key cascade is implied.
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task All_setters_read_the_old_row_and_shadow_properties_are_updated(bool async)
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            var query = db.Customers.Where(c => c.Id == 1);
            Assert.Equal(1, async
                ? await query.ExecuteUpdateAsync(s => s.SetProperty(c => c.City, c => c.Name).SetProperty(c => c.Name, "Renamed").SetProperty(c => EF.Property<string>(c, "ShadowTag"), "bulk"))
                : query.ExecuteUpdate(s => s.SetProperty(c => c.City, c => c.Name).SetProperty(c => c.Name, "Renamed").SetProperty(c => EF.Property<string>(c, "ShadowTag"), "bulk")));
            Assert.Empty(db.ChangeTracker.Entries());
        }
        store.Reopen();
        using var read = store.CreateContext();
        var result = read.Customers.Where(c => c.Id == 1).Select(c => new { c.Name, c.City, Tag = EF.Property<string>(c, "ShadowTag") }).Single();
        Assert.Equal("Renamed", result.Name);
        Assert.Equal("Alice", result.City);
        Assert.Equal("bulk", result.Tag);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Unique_index_failure_rolls_back_the_entire_bulk_command(bool async)
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            if (async) await Assert.ThrowsAsync<DbUpdateException>(() => db.Customers.ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, "Same")));
            else Assert.Throws<DbUpdateException>(() => db.Customers.ExecuteUpdate(s => s.SetProperty(c => c.Name, "Same")));
            Assert.Empty(db.ChangeTracker.Entries());
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(new[] { "Alice", "Bob", "Carol" }, read.Customers.OrderBy(c => c.Id).Select(c => c.Name).ToArray());
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Runtime_setter_failure_rolls_back_preceding_updates(bool async)
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            // Id 1 succeeds; Id 2 fails after the first native update has run.
            var query = db.Customers.OrderBy(c => c.Id);
            if (async) await Assert.ThrowsAsync<DivideByZeroException>(() => query.ExecuteUpdateAsync(s => s.SetProperty(c => c.Rating, c => 10 / (2 - c.Id))));
            else Assert.Throws<DivideByZeroException>(() => query.ExecuteUpdate(s => s.SetProperty(c => c.Rating, c => 10 / (2 - c.Id))));
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(new int?[] { 4, null, 2 }, read.Customers.OrderBy(c => c.Id).Select(c => c.Rating).ToArray());
    }

    [Fact]
    public void Paging_selects_only_targeted_rows_and_required_null_is_rejected()
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            Assert.Equal(1, db.Customers.OrderBy(c => c.Id).Skip(1).Take(1).ExecuteUpdate(s => s.SetProperty(c => c.City, "Middle")));
            Assert.Throws<DbUpdateException>(() => db.Customers.ExecuteUpdate(s => s.SetProperty(c => c.Name, (string)null!)));
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(new string?[] { "London", "Middle", null }, read.Customers.OrderBy(c => c.Id).Select(c => c.City).ToArray());
        Assert.Equal(new[] { "Alice", "Bob", "Carol" }, read.Customers.OrderBy(c => c.Id).Select(c => c.Name).ToArray());
    }

    [Fact]
    public void Bulk_commands_enlist_in_local_transaction_and_failure_aborts_it()
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            using var transaction = db.Database.BeginTransaction();
            Assert.Equal(1, db.Customers.Where(c => c.Id == 1).ExecuteDelete());
            transaction.Rollback();
        }
        using (var db = store.CreateContext())
        {
            using var transaction = db.Database.BeginTransaction();
            Assert.Equal(1, db.Customers.Where(c => c.Id == 1).ExecuteUpdate(s => s.SetProperty(c => c.City, "Pending")));
            Assert.Throws<DbUpdateException>(() => db.Customers.ExecuteUpdate(s => s.SetProperty(c => c.Name, "Duplicate")));
            Assert.Throws<InvalidOperationException>(() => transaction.Commit());
            transaction.Rollback();
        }
        store.Reopen();
        using var read = store.CreateContext();
        Assert.Equal(3, read.Customers.Count());
        Assert.Equal("London", read.Customers.Single(c => c.Id == 1).City);
    }

    [Fact]
    public async Task Cancellation_and_invalid_targets_fail_without_mutating_rows()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Customers.ExecuteDeleteAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Customers.ExecuteUpdateAsync(s => s.SetProperty(c => c.City, "Canceled"), cancellation.Token));
        Assert.Throws<InvalidOperationException>(() => db.Customers.ExecuteUpdate(s => s.SetProperty(c => c.Id, c => c.Id + 10)));
        Assert.Equal(3, db.Customers.Count());
        Assert.Equal("London", db.Customers.Single(c => c.Id == 1).City);
    }
}
