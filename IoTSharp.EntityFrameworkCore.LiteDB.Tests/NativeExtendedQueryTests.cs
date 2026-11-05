using System.Diagnostics;
using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Tests;

public sealed class NativeExtendedQueryTests
{
    [Theory, InlineData(false), InlineData(true)]
    public async Task Composite_ordering_handles_mixed_directions_nulls_and_Int64_extremes(bool async)
    {
        using var fixture = new NativeFixture();
        var source = new[]
        {
            new NativeRecord { Id = 1, Group = 1, Rank = null, Number = long.MinValue },
            new NativeRecord { Id = 2, Group = 1, Rank = 4, Number = long.MaxValue },
            new NativeRecord { Id = 3, Group = 1, Rank = 4, Number = long.MinValue },
            new NativeRecord { Id = 4, Group = 2, Rank = 2, Number = 0 },
            new NativeRecord { Id = 5, Group = 2, Rank = null, Number = -1 }
        };
        using (var seed = fixture.Create()) { seed.Rows.AddRange(source); seed.SaveChanges(); }
        fixture.Reopen();
        using var db = fixture.Create();
        using var observer = new NativeObserver(db);
        var query = db.Rows.OrderBy(r => r.Group).ThenByDescending(r => r.Rank).ThenBy(r => r.Number).Skip(1).Take(3);
        var expected = source.OrderBy(r => r.Group).ThenByDescending(r => r.Rank).ThenBy(r => r.Number)
            .Skip(1).Take(3).Select(r => r.Id);
        Assert.Equal(expected, (async ? await query.ToArrayAsync() : query.ToArray()).Select(r => r.Id));
        Assert.Contains(observer.Queries, e => e.OrderBy!.StartsWith("[", StringComparison.Ordinal) && e.RowsRead == 3);
        observer.Queries.Clear();
        var reversed = db.Rows.OrderByDescending(r => r.Group).ThenBy(r => r.Rank).ThenByDescending(r => r.Number);
        Assert.Equal(source.OrderByDescending(r => r.Group).ThenBy(r => r.Rank).ThenByDescending(r => r.Number).Select(r => r.Id),
            (async ? await reversed.ToArrayAsync() : reversed.ToArray()).Select(r => r.Id));
        Assert.Single(observer.Queries);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task DateTime_ticks_and_time_values_filter_order_and_rebind_after_reopen(bool async)
    {
        using var fixture = new NativeFixture();
        var origin = new DateTime(2031, 2, 3, 4, 5, 6, DateTimeKind.Utc).AddTicks(7);
        var source = new[]
        {
            new NativeRecord { Id = 1, At = origin.AddTicks(-1), Duration = TimeSpan.MinValue, Time = TimeOnly.MinValue },
            new NativeRecord { Id = 2, At = DateTime.SpecifyKind(origin, DateTimeKind.Unspecified), Duration = TimeSpan.Zero, Time = new TimeOnly(12, 0) },
            new NativeRecord { Id = 3, At = origin.AddTicks(1), Duration = TimeSpan.MaxValue, Time = TimeOnly.MaxValue }
        };
        using (var seed = fixture.Create()) { seed.Rows.AddRange(source); seed.SaveChanges(); }
        fixture.Reopen();
        using var db = fixture.Create();
        using var observer = new NativeObserver(db);
        var threshold = origin;
        var query = db.Rows.Where(r => r.At >= threshold).OrderByDescending(r => r.At).ThenBy(r => r.Id);
        Assert.Equal(new[] { 3, 2 }, (async ? await query.ToArrayAsync() : query.ToArray()).Select(r => r.Id));
        threshold = origin.AddTicks(1);
        Assert.Equal(3, Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()).Id);
        Assert.Equal(2, async ? await db.Rows.CountAsync(r => r.Duration >= TimeSpan.Zero && r.Time >= new TimeOnly(12, 0))
            : db.Rows.Count(r => r.Duration >= TimeSpan.Zero && r.Time >= new TimeOnly(12, 0)));
        Assert.Contains(observer.Queries, e => e.Predicate!.Contains(".ticks", StringComparison.Ordinal) && e.RowsRead == 1);
        Assert.Contains("Count", observer.Operations);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Exact_decimal_aggregates_and_numeric_extrema_execute_in_LiteDB(bool async)
    {
        using var fixture = new NativeFixture();
        using (var seed = fixture.Create())
        {
            seed.Rows.AddRange(new NativeRecord { Amount = 1.0000000000000000001m, Number = long.MinValue },
                new NativeRecord { Amount = 2.0000000000000000001m, Number = long.MaxValue },
                new NativeRecord { Amount = null, Number = 0 });
            seed.SaveChanges();
        }
        fixture.Reopen();
        using var db = fixture.Create();
        using var observer = new NativeObserver(db);
        Assert.Equal(3.0000000000000000002m, async ? await db.Rows.SumAsync(r => r.Amount) : db.Rows.Sum(r => r.Amount));
        Assert.Equal(1.5000000000000000001m, async ? await db.Rows.AverageAsync(r => r.Amount) : db.Rows.Average(r => r.Amount));
        Assert.Equal(long.MinValue, async ? await db.Rows.MinAsync(r => r.Number) : db.Rows.Min(r => r.Number));
        Assert.Equal(long.MaxValue, async ? await db.Rows.MaxAsync(r => r.Number) : db.Rows.Max(r => r.Number));
        Assert.Equal(0m, async ? await db.Rows.Where(r => r.Id < 0).SumAsync(r => r.Amount) : db.Rows.Where(r => r.Id < 0).Sum(r => r.Amount));
        Assert.Null(async ? await db.Rows.Where(r => r.Id < 0).AverageAsync(r => r.Amount) : db.Rows.Where(r => r.Id < 0).Average(r => r.Amount));
        Assert.Null(async ? await db.Rows.Where(r => r.Amount == null).AverageAsync(r => r.Amount) : db.Rows.Where(r => r.Amount == null).Average(r => r.Amount));
        Assert.Null(async ? await db.Rows.Where(r => r.Id < 0).MinAsync(r => r.Amount) : db.Rows.Where(r => r.Id < 0).Min(r => r.Amount));
        if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => db.Rows.Where(r => r.Id < 0).MinAsync(r => r.Number));
        else Assert.Throws<InvalidOperationException>(() => db.Rows.Where(r => r.Id < 0).Min(r => r.Number));
        // Nullable decimal null-equality currently remains in the CLR predicate path.
        Assert.Equal(new[] { "Sum", "Average", "Min", "Max", "Sum", "Average", "Min" }, observer.Operations);
        Assert.Empty(observer.Queries);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task Decimal_sum_overflow_preserves_CLR_failure_without_changing_documents(bool async)
    {
        using var fixture = new NativeFixture();
        using (var seed = fixture.Create())
        {
            seed.Rows.AddRange(new NativeRecord { Amount = decimal.MaxValue }, new NativeRecord { Amount = decimal.MaxValue });
            seed.SaveChanges();
        }
        fixture.Reopen();
        using var db = fixture.Create();
        if (async) await Assert.ThrowsAsync<OverflowException>(() => db.Rows.SumAsync(r => r.Amount));
        else Assert.Throws<OverflowException>(() => db.Rows.Sum(r => r.Amount));
        Assert.Equal(2, db.Rows.Count());
        Assert.All(db.Rows.AsNoTracking(), row => Assert.Equal(decimal.MaxValue, row.Amount));
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task String_culture_and_case_semantics_remain_in_CLR_path(bool async)
    {
        using var fixture = new NativeFixture();
        var source = new[] { "a", "A", "ä", "z", "I", "ı", "i", "İ", "", null };
        using (var seed = fixture.Create())
        {
            seed.Rows.AddRange(source.Select((text, index) => new NativeRecord { Id = index + 1, Text = text }));
            seed.SaveChanges();
        }
        fixture.Reopen();
        using var db = fixture.Create();
        using var observer = new NativeObserver(db);
        var query = db.Rows.OrderBy(r => r.Text).ThenBy(r => r.Id);
        Assert.Equal(source.Select((text, index) => (Text: text, Id: index + 1)).OrderBy(r => r.Text).ThenBy(r => r.Id).Select(r => r.Id),
            (async ? await query.ToArrayAsync() : query.ToArray()).Select(r => r.Id));
        Assert.Equal(1, async ? await db.Rows.CountAsync(r => r.Text == "a") : db.Rows.Count(r => r.Text == "a"));
        Assert.Empty(observer.Queries);
        Assert.Empty(observer.Operations);
    }

    [Fact]
    public void Diagnostics_are_correlated_to_the_issuing_context()
    {
        using var fixture = new NativeFixture();
        using var first = fixture.Create();
        using var second = fixture.Create();
        using var observer = new NativeObserver(first);
        first.Rows.Count();
        second.Rows.LongCount();
        Assert.Equal(new[] { "Count" }, observer.Operations);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task DateTimeOffset_comparison_preserves_offsets_and_submillisecond_ticks_in_CLR(bool async)
    {
        using var fixture = new NativeFixture();
        var instant = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero).AddTicks(1);
        using (var seed = fixture.Create())
        {
            seed.Rows.AddRange(new NativeRecord { Id = 1, Offset = instant.ToOffset(TimeSpan.FromHours(8)) },
                new NativeRecord { Id = 2, Offset = instant }, new NativeRecord { Id = 3, Offset = instant.AddTicks(1) });
            seed.SaveChanges();
        }
        fixture.Reopen();
        using var db = fixture.Create();
        using var observer = new NativeObserver(db);
        Assert.Equal(2, async ? await db.Rows.CountAsync(r => r.Offset == instant) : db.Rows.Count(r => r.Offset == instant));
        var query = db.Rows.Where(r => r.Offset > instant);
        Assert.Equal(3, Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()).Id);
        Assert.Empty(observer.Queries);
        Assert.Empty(observer.Operations);
    }

    private sealed class NativeFixture : IDisposable
    {
        private LiteDBDatabaseRoot _root = new();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "iotsharp-native-" + Guid.NewGuid().ToString("N"));
        public NativeFixture() => Directory.CreateDirectory(_directory);
        public NativeContext Create() => new(Path.Combine(_directory, "native.db"), _root);
        public void Reopen() { _root.Dispose(); _root = new(); }
        public void Dispose() { _root.Dispose(); Directory.Delete(_directory, recursive: true); }
    }

    private sealed class NativeContext(string path, LiteDBDatabaseRoot root) : DbContext
    {
        public DbSet<NativeRecord> Rows => Set<NativeRecord>();
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseLiteDB(path, root);
    }

    private sealed class NativeRecord
    {
        public int Id { get; set; }
        public int Group { get; set; }
        public int? Rank { get; set; }
        public long Number { get; set; }
        public DateTime At { get; set; }
        public TimeSpan Duration { get; set; }
        public TimeOnly Time { get; set; }
        public DateTimeOffset Offset { get; set; }
        public decimal? Amount { get; set; }
        public string? Text { get; set; }
    }

    private sealed class NativeObserver : IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly DbContextId _contextId;
        private readonly IDisposable _subscription;
        public List<(string? Predicate, string? OrderBy, int RowsRead)> Queries { get; } = [];
        public List<string> Operations { get; } = [];
        public NativeObserver(DbContext context)
        {
            _contextId = context.ContextId;
            var listener = (DiagnosticListener)context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>().DiagnosticSource;
            _subscription = listener.Subscribe(this);
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value == null) return;
            var type = value.Value.GetType();
            if (type.GetProperty("ContextId")?.GetValue(value.Value) is not DbContextId id || id != _contextId) return;
            if (value.Key == "IoTSharp.EntityFrameworkCore.LiteDB.QueryExecuted")
                Queries.Add(((string?)type.GetProperty("Predicate")!.GetValue(value.Value),
                    (string?)type.GetProperty("OrderBy")!.GetValue(value.Value), (int)type.GetProperty("RowsRead")!.GetValue(value.Value)!));
            if (value.Key == "IoTSharp.EntityFrameworkCore.LiteDB.AggregateExecuted")
                Operations.Add((string)type.GetProperty("Operation")!.GetValue(value.Value)!);
        }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose() => _subscription.Dispose();
    }
}
