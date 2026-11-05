using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class EngineBoundaryTests
{
    [Fact]
    public void Nested_local_transaction_is_rejected_without_losing_outer_transaction()
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            using var transaction = db.Database.BeginTransaction();
            Assert.Throws<InvalidOperationException>(() => db.Database.BeginTransaction());
            db.Customers.Add(new Customer { Name = "Outer transaction survives" });
            Assert.Equal(1, db.SaveChanges());
            transaction.Rollback();
        }
        store.Reopen();
        using var reopened = store.CreateContext();
        Assert.Equal(3, reopened.Customers.Count());
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Database_has_no_cross_collection_foreign_key_or_unloaded_cascade(bool async)
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            // SaveChanges only receives this dependent; LiteDB has no cross-collection FK constraint.
            db.Orders.Add(new Order { Id = 100, CustomerId = 9999, Amount = 42 });
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
            var customer = async ? await db.Customers.SingleAsync(c => c.Id == 1) : db.Customers.Single(c => c.Id == 1);
            db.Remove(customer);
            Assert.Equal(1, async ? await db.SaveChangesAsync() : db.SaveChanges());
        }
        store.Reopen();
        using var reopened = store.CreateContext();
        Assert.False(async ? await reopened.Customers.AnyAsync(c => c.Id == 1) : reopened.Customers.Any(c => c.Id == 1));
        Assert.Equal(2, async ? await reopened.Orders.CountAsync(o => o.CustomerId == 1) : reopened.Orders.Count(o => o.CustomerId == 1));
        Assert.True(async ? await reopened.Orders.AnyAsync(o => o.CustomerId == 9999) : reopened.Orders.Any(o => o.CustomerId == 9999));
    }
}
