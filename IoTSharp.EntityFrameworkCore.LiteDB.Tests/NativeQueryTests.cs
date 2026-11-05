using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class NativeQueryTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Numeric_filter_order_and_paging_are_executed_by_LiteDB(bool async)
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var observer = new QueryObserver(db.ContextId);
        var listener = (DiagnosticListener)db.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>().DiagnosticSource;
        using var subscription = listener.Subscribe(observer);
        var threshold = 0;
        var skip = 1;
        var query = db.Orders.Where(o => o.Amount > threshold).OrderByDescending(o => o.Amount).Skip(skip).Take(1);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(20, Assert.Single(rows).Amount);
        Assert.Contains(observer.Events, value =>
            (int)value.GetType().GetProperty("RowsRead")!.GetValue(value)! == 1
            && value.GetType().GetProperty("Predicate")!.GetValue(value) is string predicate && predicate.Contains("@p"));
    }

    [Fact]
    public void Native_nullable_comparisons_and_zero_take_match_LINQ()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        Assert.Equal(1, db.Customers.Count(c => c.Rating > 2));
        Assert.Equal(2, db.Customers.Count(c => c.Rating != 2));
        Assert.Empty(db.Customers.OrderBy(c => c.Id).Take(0).ToArray());
        Assert.Equal(new[] { 2, 3 }, db.Customers.Where(c => c.Id > 1).OrderBy(c => c.Id).Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Count_long_count_and_any_use_native_aggregates()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        var observer = new QueryObserver(db.ContextId);
        var listener = (DiagnosticListener)db.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>().DiagnosticSource;
        using var subscription = listener.Subscribe(observer);
        Assert.Equal(2, db.Orders.Count(o => o.Amount >= 20));
        Assert.Equal(3L, db.Orders.LongCount());
        Assert.True(db.Orders.Any(o => o.Id == 1));
        Assert.Equal(new[] { "Count", "LongCount", "Any" }, observer.Operations);
    }

    [Fact]
    public void Native_diagnostics_ignore_queries_from_other_contexts()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        using var other = store.CreateContext();
        var observer = new QueryObserver(db.ContextId);
        var listener = (DiagnosticListener)db.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>().DiagnosticSource;
        using var subscription = listener.Subscribe(observer);

        Assert.Equal(3, other.Orders.Count());
        Assert.Equal(3L, db.Orders.LongCount());
        Assert.True(other.Orders.Any());
        Assert.Single(other.Orders.OrderBy(o => o.Id).Take(1).ToList());
        Assert.Single(db.Orders.OrderBy(o => o.Id).Take(1).ToList());

        Assert.Equal(new[] { "LongCount" }, observer.Operations);
        Assert.Single(observer.Events);
    }

    [Fact]
    public void Numeric_casts_and_NaN_preserve_CLR_comparison_semantics()
    {
        using var store = new ProviderTestStore();
        using var db = store.CreateContext();
        Assert.Equal(1, db.Customers.Count(c => (double)c.Id < 1.5));
        db.Scalars.AddRange(new ScalarEntity { Double = double.NaN }, new ScalarEntity { Double = -1 });
        db.SaveChanges();
        Assert.Equal(1, db.Scalars.Count(s => s.Double < 0));
        Assert.Equal(0, db.Scalars.Count(s => s.Double == double.NaN));
    }

    private sealed class QueryObserver(DbContextId contextId) : IObserver<KeyValuePair<string, object?>>
    {
        public List<object> Events { get; } = [];
        public List<string> Operations { get; } = [];
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value?.GetType().GetProperty("ContextId")?.GetValue(value.Value) is not DbContextId eventContext
                || eventContext != contextId) return;
            if (value.Key == "IoTSharp.EntityFrameworkCore.LiteDB.QueryExecuted" && value.Value != null) Events.Add(value.Value);
            if (value.Key == "IoTSharp.EntityFrameworkCore.LiteDB.AggregateExecuted" && value.Value != null)
                Operations.Add((string)value.Value.GetType().GetProperty("Operation")!.GetValue(value.Value)!);
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
