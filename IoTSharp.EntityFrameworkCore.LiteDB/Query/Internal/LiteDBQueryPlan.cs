using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

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
    private bool _scalar;

    public static bool TryCreate(Expression expression, out LiteDBQueryPlan plan, bool scalar = false)
    {
        plan = new LiteDBQueryPlan { _scalar = scalar };
        return plan.Parse(expression, 0) && plan.EntityType.FindDiscriminatorProperty() == null;
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
            case nameof(Enumerable.Select) when _scalar:
                var projection = call.Arguments[1].UnwrapLambdaFromQuote();
                return projection.Body is NewExpression { Type: var bufferType, Arguments.Count: 1 } projected
                    && bufferType == typeof(ValueBuffer) && projected.Arguments[0] is NewArrayExpression array
                    && array.Expressions.All(value => StripConvert(value) is ConstantExpression || TryProperty(value, out _));
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
                OrderBy = "$." + property.Name;
                Descending = call.Method.Name == nameof(Enumerable.OrderByDescending);
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
                sql = $"$.{property.Name} {op} @p{parameter}";
                if (binary.NodeType is not ExpressionType.Equal and not ExpressionType.NotEqual)
                    sql = $"($.{property.Name} != null AND @p{parameter} != null AND ({sql}))";
                return true;
            }
        }
        sql = null;
        return false;
    }

    private bool TryProperty(Expression expression, out IProperty property)
    {
        var originalType = expression.Type.UnwrapNullableType();
        expression = StripConvert(expression);
        if (expression is MethodCallExpression call && call.Method.Name == "ValueBufferTryReadValue"
            && call.Arguments.Count == 3 && call.Arguments[2] is ConstantExpression { Value: IProperty candidate }
            && candidate.GetTypeMapping().Converter == null
            && (candidate.ClrType.UnwrapNullableType() == originalType || originalType == typeof(object)))
        {
            var type = candidate.ClrType.UnwrapNullableType();
            // BSON comparisons have different string/culture and special-type semantics. Keep those in the EF projection path.
            if ((type.IsInteger() || type == typeof(decimal))
                && candidate.Name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                property = candidate;
                return true;
            }
        }
        property = null;
        return false;
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
