using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class AdvancedMappingTests
{
    [Fact]
    public void Primitive_collections_are_saved_queried_and_change_tracked()
    {
        using var store = new ProviderTestStore(false);
        using (var db = store.CreateContext())
        {
            db.Add(new ScalarEntity { Integers = [1, 2], Words = ["red", "blue"] });
            db.SaveChanges();
        }
        store.Reopen();
        using (var db = store.CreateContext())
        {
            var entity = db.Scalars.Where(s => s.Integers.Contains(2)).Single();
            Assert.Equal(new[] { "red", "blue" }, entity.Words);
            entity.Integers.Add(3);
            entity.Words[0] = "green";
            Assert.Equal(1, db.SaveChanges());
        }
        store.Reopen();
        using var verify = store.CreateContext();
        Assert.Equal(new[] { 1, 2, 3 }, verify.Scalars.Single().Integers);
        Assert.Equal("green", verify.Scalars.Single().Words[0]);
    }

    [Fact]
    public void Owned_values_and_many_to_many_links_round_trip()
    {
        using var store = new ProviderTestStore(false);
        using (var db = store.CreateContext())
        {
            db.Add(new Owner { Address = new Address { Street = "Main Street" }, Tags = [new Tag { Name = "EF" }, new Tag { Name = "LiteDB" }] });
            db.SaveChanges();
        }
        store.Reopen();
        using var verify = store.CreateContext();
        var owner = verify.Set<Owner>().Include(o => o.Tags).Single();
        Assert.Equal("Main Street", owner.Address.Street);
        Assert.Equal(new[] { "EF", "LiteDB" }, owner.Tags.Select(t => t.Name).OrderBy(n => n));
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task EnsureCreated_runs_matching_seeding_delegate_and_persists_marker(bool async)
    {
        using var store = new ProviderTestStore(false);
        var calls = 0;
        using (var db = store.CreateContext(options => options
            .UseSeeding((context, created) => { Assert.True(created); calls++; })
            .UseAsyncSeeding((context, created, cancellation) => { Assert.True(created); calls++; return Task.CompletedTask; })))
        {
            Assert.True(async ? await db.Database.EnsureCreatedAsync() : db.Database.EnsureCreated());
        }
        Assert.Equal(1, calls);
        store.Reopen();
        using var verify = store.CreateContext();
        Assert.False(verify.Database.EnsureCreated());
    }

    [Fact]
    public void EnsureDeleted_drops_data_even_after_reopen_without_prior_query()
    {
        using var store = new ProviderTestStore();
        store.Reopen();
        using (var db = store.CreateContext()) Assert.True(db.Database.EnsureDeleted());
        store.Reopen();
        using var verify = store.CreateContext();
        Assert.Empty(verify.Customers);
    }

    [Fact]
    public void Failed_save_inside_explicit_transaction_rolls_back_and_prevents_commit()
    {
        using var store = new ProviderTestStore();
        using (var db = store.CreateContext())
        {
            using var tx = db.Database.BeginTransaction();
            db.Add(new Customer { Id = 20, Name = "BatchOne" });
            db.SaveChanges();
            db.AddRange(new Customer { Id = 21, Name = "BatchTwo" }, new Customer { Id = 22, Name = "Alice" });
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
            Assert.Throws<InvalidOperationException>(() => tx.Commit());
        }
        store.Reopen();
        using var verify = store.CreateContext();
        Assert.Equal(3, verify.Customers.Count());
    }

    [Fact]
    public void Another_context_cannot_join_an_explicit_transaction_accidentally()
    {
        using var store = new ProviderTestStore();
        using var first = store.CreateContext();
        using var second = store.CreateContext();
        using var tx = first.Database.BeginTransaction();
        Assert.Throws<InvalidOperationException>(() => second.Customers.ToList());
        tx.Rollback();
        Assert.Equal(3, second.Customers.Count());
    }
}
