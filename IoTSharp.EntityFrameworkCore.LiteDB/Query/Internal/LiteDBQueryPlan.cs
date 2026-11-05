using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;
using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal;

/// <summary>A parameterized subset of EF query operations which LiteDB can execute with matching semantics.</summary>
internal sealed class LiteDBQueryPlan
{
    public IEntityType EntityType { get; private set; }
    public string Predicate { get; private set; }
    public string OrderBy { get; private set; }
    public bool Descending { get; private set; }
    public int? SkipParameter { get; private set; }
    public int? TakeParameter { get; private set; }
    public List<Expression> Parameters { get; } = [];
    public List<IProperty> ParameterProperties { get; } = [];
    public IProperty AggregateProperty { get; private set; }
    public Type AggregateResultType { get; private set; }
    private bool _scalar;
    private readonly List<(IProperty Property, bool Descending)> _ordering = [];

    public static bool TryCreate(Expression expression, out LiteDBQueryPlan plan, bool scalar = false)
    {
        plan = new LiteDBQueryPlan { _scalar = scalar };
        return plan.Parse(expression, 0) && plan.EntityType.FindDiscriminatorProperty() == null;
    }

    public static bool TryCreateAggregate(MethodCallExpression call, out LiteDBQueryPlan plan)
    {
        plan = null;
        if (call.Method.DeclaringType != typeof(Enumerable) || call.Arguments.Count != 2
            || call.Method.Name is not (nameof(Enumerable.Sum) or nameof(Enumerable.Average)
                or nameof(Enumerable.Min) or nameof(Enumerable.Max))
            || !TryCreate(call.Arguments[0], out var candidate, scalar: true)
            || candidate.SkipParameter != null || candidate.TakeParameter != null) return false;
        var selector = call.Arguments[1].UnwrapLambdaFromQuote();
        if (!candidate.TryProperty(selector.Body, out var property)) return false;
        var type = property.ClrType.UnwrapNullableType();
        // Integer Sum/Average have overflow and rounding contracts unlike BSON aggregates.
        // Decimal SUM/AVG and exact numeric MIN/MAX can preserve the CLR result contract.
        if (call.Method.Name is nameof(Enumerable.Sum) or nameof(Enumerable.Average)
            ? type != typeof(decimal) : !(type.IsInteger() || type == typeof(decimal))) return false;
        candidate.AggregateProperty = property;
        candidate.AggregateResultType = call.Type;
        plan = candidate;
        return true;
    }

    private bool Parse(Expression expression, int depth)
    {
        if (depth > 64) return false;
        if (expression is LiteDBTableExpression table)
        {
            EntityType = table.EntityType;
            return true;
        }
        if (expression is not MethodCallExpression call || call.Method.DeclaringType != typeof(Enumerable)
            || !Parse(call.Arguments[0], depth + 1)) return false;

        switch (call.Method.Name)
        {
            case nameof(Enumerable.Select):
                var projection = call.Arguments[1].UnwrapLambdaFromQuote();
                return projection.Body is NewExpression { Type: var bufferType, Arguments.Count: 1 } projected
                    && bufferType == typeof(ValueBuffer) && projected.Arguments[0] is NewArrayExpression array
                    && (_scalar
                        ? array.Expressions.All(value => StripConvert(value) is ConstantExpression || TryProperty(value, out _))
                        : IsIdentityProjection(array));
            case nameof(Enumerable.Where) when SkipParameter == null && TakeParameter == null:
                var lambda = call.Arguments[1].UnwrapLambdaFromQuote();
                if (!TryPredicate(lambda.Body, out var filter)) return false;
                Predicate = Predicate == null ? filter : $"({Predicate}) AND ({filter})";
                return true;
            case nameof(Enumerable.OrderBy):
            case nameof(Enumerable.OrderByDescending):
                if (SkipParameter != null || TakeParameter != null) return false;
                var key = call.Arguments[1].UnwrapLambdaFromQuote();
                if (!TryProperty(key.Body, out var property)) return false;
                _ordering.Clear();
                _ordering.Add((property, call.Method.Name == nameof(Enumerable.OrderByDescending)));
                OrderBy = NativePath(property);
                Descending = call.Method.Name == nameof(Enumerable.OrderByDescending);
                return true;
            case nameof(Enumerable.ThenBy):
            case nameof(Enumerable.ThenByDescending):
                if (SkipParameter != null || TakeParameter != null || _ordering.Count == 0) return false;
                var nextKey = call.Arguments[1].UnwrapLambdaFromQuote();
                if (!TryProperty(nextKey.Body, out var nextProperty)) return false;
                _ordering.Add((nextProperty, call.Method.Name == nameof(Enumerable.ThenByDescending)));
                // LiteDB exposes one OrderBy. BSON arrays compare lexicographically, so a composite
                // key preserves ThenBy while decimal negation avoids Int64.MinValue overflow.
                OrderBy = "[" + string.Join(", ", _ordering.Select(order => SortKey(order.Property, order.Descending))) + "]";
                Descending = false;
                return true;
            case nameof(Enumerable.Skip) when SkipParameter == null && TakeParameter == null:
                SkipParameter = AddParameter(call.Arguments[1], null);
                return true;
            case nameof(Enumerable.Take) when TakeParameter == null:
                TakeParameter = AddParameter(call.Arguments[1], null);
                return true;
            default: return false;
        }
    }

    private bool IsIdentityProjection(NewArrayExpression array)
    {
        // The store returns the complete metadata-indexed row. A root Select can be removed only
        // if it keeps that exact shape; arbitrary projections still run in the EF shaper pipeline.
        if (array.Expressions.Count != EntityType.GetProperties().Count()) return false;
        for (var i = 0; i < array.Expressions.Count; i++)
        {
            var value = array.Expressions[i];
            if (StripConvert(value) is not MethodCallExpression { Method.Name: "ValueBufferTryReadValue", Arguments.Count: 3 } read
                || read.Arguments[1] is not ConstantExpression { Value: int index } || index != i
                || read.Arguments[2] is not ConstantExpression { Value: IProperty property }
                || property.DeclaringType is not IEntityType || property.GetIndex() != i
                || !HasOnlyIdentityOrBoxingConversions(value, property.ClrType.UnwrapNullableType())) return false;
        }
        return true;
    }

    private bool TryPredicate(Expression expression, out string sql)
    {
        expression = StripConvert(expression);
        if (expression is MethodCallExpression { Method.Name: "Equals", Arguments.Count: 2, Object: ConstantExpression { Value: ValueComparer comparer } } equality
            && comparer.GetType().Name.StartsWith("Default", StringComparison.Ordinal))
        {
            return TryPredicate(Expression.Equal(equality.Arguments[0], equality.Arguments[1]), out sql);
        }
        if (expression is BinaryExpression binary)
        {
            if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse
                && TryPredicate(binary.Left, out var left) && TryPredicate(binary.Right, out var right))
            {
                sql = $"({left}) {(binary.NodeType == ExpressionType.AndAlso ? "AND" : "OR")} ({right})";
                return true;
            }
            if (TryProperty(binary.Left, out var property) && !ContainsRow(binary.Right)
                && binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.LessThan
                    or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual)
            {
                var parameter = AddParameter(binary.Right, property);
                var op = binary.NodeType switch { ExpressionType.Equal => "=", ExpressionType.NotEqual => "!=", ExpressionType.LessThan => "<", ExpressionType.LessThanOrEqual => "<=", ExpressionType.GreaterThan => ">", _ => ">=" };
                var path = NativePath(property);
                sql = $"{path} {op} @p{parameter}";
                if (binary.NodeType is not ExpressionType.Equal and not ExpressionType.NotEqual)
                    sql = $"({path} != null AND @p{parameter} != null AND ({sql}))";
                return true;
            }
        }
        sql = null;
        return false;
    }

    private bool TryProperty(Expression expression, out IProperty property)
    {
        var originalExpression = expression;
        var originalType = expression.Type.UnwrapNullableType();
        expression = StripConvert(expression);
        if (expression is MethodCallExpression call && call.Method.Name == "ValueBufferTryReadValue"
            && call.Arguments.Count == 3 && call.Arguments[2] is ConstantExpression { Value: IProperty candidate }
            && candidate.DeclaringType is IEntityType
            && candidate.GetTypeMapping().Converter == null
            && (candidate.ClrType.UnwrapNullableType() == originalType || originalType == typeof(object))
            && HasOnlyIdentityOrBoxingConversions(originalExpression, candidate.ClrType.UnwrapNullableType()))
        {
            var type = candidate.ClrType.UnwrapNullableType();
            // BSON comparisons have different string/culture and special-type semantics. Keep those in the EF projection path.
            if ((type.IsInteger() || type == typeof(decimal) || type == typeof(DateTime)
                    || type == typeof(TimeOnly) || type == typeof(TimeSpan))
                && candidate.Name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                property = candidate;
                return true;
            }
        }
        property = null;
        return false;
    }

    private static bool HasOnlyIdentityOrBoxingConversions(Expression expression, Type propertyType)
    {
        for (var depth = 0; depth < 64 && expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary; depth++)
        {
            if (unary.Type.UnwrapNullableType() != propertyType && unary.Type != typeof(object)) return false;
            expression = unary.Operand;
        }
        return true;
    }

    public BsonValue CreateParameter(int index, object value)
    {
        if (ParameterProperties[index] is not { } property) return new BsonValue(value);
        var serialized = LiteDBValueSerialization.Write(property, value);
        return property.ClrType.UnwrapNullableType() == typeof(DateTime) && !serialized.IsNull
            ? serialized["ticks"] : serialized;
    }

    private static string NativePath(IProperty property)
        => "$." + property.Name + (property.ClrType.UnwrapNullableType() == typeof(DateTime) ? ".ticks" : "");

    private static string SortKey(IProperty property, bool descending)
    {
        var path = NativePath(property);
        // Each key has a null-rank and a numeric value. Reversing only the number would
        // incorrectly leave null first for descending nullable keys.
        return descending
            ? $"[IIF({path} = null, 1, 0), IIF({path} = null, 0, 0 - DECIMAL({path}))]"
            : $"[IIF({path} = null, 0, 1), IIF({path} = null, 0, DECIMAL({path}))]";
    }

    private int AddParameter(Expression value, IProperty property)
    {
        Parameters.Add(Expression.Convert(value, typeof(object)));
        ParameterProperties.Add(property);
        return Parameters.Count - 1;
    }

    private static Expression StripConvert(Expression expression)
    {
        for (var depth = 0; depth < 64 && expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary; depth++)
            expression = unary.Operand;
        return expression;
    }

    private static bool ContainsRow(Expression expression)
    {
        var finder = new RowFinder();
        finder.Visit(expression);
        return finder.Found;
    }

    private sealed class RowFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node.Type == typeof(ValueBuffer)) Found = true;
            return node;
        }
    }
}
