using System.Globalization;
using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.Native;

internal static class LiteDBNativeAggregate
{
    public static object Execute(ILiteCollection<BsonDocument> documents, LiteDBQueryPlan plan, object[] values, string operation)
    {
        var parameters = new BsonDocument();
        for (var i = 0; i < values.Length; i++) parameters[$"p{i}"] = plan.CreateParameter(i, values[i]);
        var query = documents.Query();
        if (plan.Predicate != null) query = query.Where(plan.Predicate, parameters);
        // LiteDB AVG returns numeric zero on an empty sequence; CLR nullable aggregates return
        // null (and non-nullable MIN/MAX/AVG throw). Test emptiness before the aggregate.
        query = query.Where($"$.{plan.AggregateProperty.Name} != null");
        var existenceQuery = documents.Query().Where($"$.{plan.AggregateProperty.Name} != null");
        if (plan.Predicate != null) existenceQuery = existenceQuery.Where(plan.Predicate, parameters);
        if (!existenceQuery.Exists())
        {
            if (operation == nameof(Enumerable.Sum)) return decimal.Zero;
            if (Nullable.GetUnderlyingType(plan.AggregateResultType) != null) return null;
            throw new InvalidOperationException("Sequence contains no elements");
        }
        var function = operation == nameof(Enumerable.Average) ? "AVG" : operation.ToUpperInvariant();
        var result = query.Select($"{{value: {function}(*.{plan.AggregateProperty.Name})}}")
            .ToEnumerable().FirstOrDefault()?["value"] ?? BsonValue.Null;
        if (result.IsNull)
        {
            if (operation == nameof(Enumerable.Sum)) return decimal.Zero;
            if (Nullable.GetUnderlyingType(plan.AggregateResultType) != null) return null;
            throw new InvalidOperationException("Sequence contains no elements");
        }
        return Convert.ChangeType(result.RawValue, plan.AggregateResultType.UnwrapNullableType(), CultureInfo.InvariantCulture);
    }
}
