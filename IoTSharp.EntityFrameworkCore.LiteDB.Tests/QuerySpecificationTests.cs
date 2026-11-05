// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from EF Core v10.0.14 NorthwindWhereQueryTestBase. See UPSTREAM.md.
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class QuerySpecificationTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Where_simple(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var query = db.Customers.Where(c => c.City == "London");
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(1, Assert.Single(rows).Id);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Where_simple_closure_via_query_cache(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var city = "London";
        for (var i = 0; i < 2; i++)
        {
            var query = db.Customers.Where(c => c.City == city);
            var rows = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(i + 1, Assert.Single(rows).Id);
            city = "Seattle";
        }
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Where_indexer_closure(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var cities = new[] { "London" };
        var query = db.Customers.Where(c => c.City == cities[0]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(1, Assert.Single(rows).Id);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Where_dictionary_key_access_closure(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var predicateMap = new Dictionary<string, string> { ["City"] = "London" };
        var query = db.Customers.Where(c => c.City == predicateMap["City"]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(1, Assert.Single(rows).Id);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Null_semantics_and_nullable_predicate(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var query = db.Customers.Where(c => c.City == null || c.Rating == null).OrderBy(c => c.Id).Select(c => c.Id);
        Assert.Equal(new[] { 2, 3 }, async ? await query.ToArrayAsync() : query.ToArray());
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Ordering_paging_and_projection(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var query = db.Customers.OrderBy(c => c.City).ThenByDescending(c => c.Id).Skip(1).Take(2).Select(c => c.Name);
        Assert.Equal(new[] { "Alice", "Bob" }, async ? await query.ToArrayAsync() : query.ToArray());
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Collection_contains_and_string_functions(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        int[] ids = [1, 3];
        var query = db.Customers.Where(c => ids.Contains(c.Id) && c.Name.StartsWith("A")).Select(c => c.Name.ToUpper());
        Assert.Equal("ALICE", Assert.Single(async ? await query.ToListAsync() : query.ToList()));
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Include_reference_and_collection(bool async)
    {
        using var store = new ProviderTestStore();
        store.Reopen();
        using var db = store.CreateContext();
        var customers = db.Customers.Include(c => c.Orders).OrderBy(c => c.Id);
        var rows = async ? await customers.ToListAsync() : customers.ToList();
        Assert.Equal(2, rows[0].Orders.Count);
        Assert.Single(rows[1].Orders);
        Assert.Empty(rows[2].Orders);
        Assert.Same(rows[0], rows[0].Orders[0].Customer);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Join_and_grouped_aggregates(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var query = db.Customers.Join(db.Orders, c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount })
            .GroupBy(x => x.Name).Select(g => new { Name = g.Key, Sum = g.Sum(x => x.Amount) }).OrderBy(x => x.Name);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(30, rows[0].Sum);
        Assert.Equal(30, rows[1].Sum);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Distinct_union_and_aggregates(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var query = db.Customers.Where(c => c.Id < 3).Select(c => c.Id).Union(db.Customers.Where(c => c.Id > 1).Select(c => c.Id));
        Assert.Equal(3, async ? await query.CountAsync() : query.Count());
        Assert.Equal(60m, async ? await db.Orders.SumAsync(o => o.Amount) : db.Orders.Sum(o => o.Amount));
        Assert.Equal(20m, async ? await db.Orders.AverageAsync(o => o.Amount) : db.Orders.Average(o => o.Amount));
    }

    [Fact]
    public void Tracking_no_tracking_and_compiled_queries()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        Assert.Same(db.Customers.First(), db.Customers.First());
        Assert.NotSame(db.Customers.AsNoTracking().First(), db.Customers.AsNoTracking().First());
        var query = EF.CompileQuery((ProviderContext context, int id) => context.Customers.Single(c => c.Id == id));
        Assert.Equal("Alice", query(db, 1).Name);
        Assert.Equal("Bob", query(db, 2).Name);
    }
}
