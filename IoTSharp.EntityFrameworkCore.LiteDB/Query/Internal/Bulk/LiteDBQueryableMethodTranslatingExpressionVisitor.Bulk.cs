namespace IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal;

internal partial class LiteDBQueryableMethodTranslatingExpressionVisitor
{
    protected override Expression? TranslateExecuteDelete(ShapedQueryExpression source)
        => CreateBulk(source, []);

    protected override Expression? TranslateExecuteUpdate(ShapedQueryExpression source, IReadOnlyList<ExecuteUpdateSetter> setters)
        => CreateBulk(source, setters);

    private Expression? CreateBulk(ShapedQueryExpression source, IReadOnlyList<ExecuteUpdateSetter> setters)
    {
        if (source.ShaperExpression is not StructuralTypeShaperExpression { StructuralType: IEntityType entityType }
            || source.QueryExpression is not LiteDBQueryExpression query)
        {
            AddTranslationErrorDetails("LiteDB bulk commands require an entity query; projected shapes are not update targets.");
            return null;
        }
        // Separate collections represent concrete types; a command must target exactly one collection.
        if (entityType.BaseType != null || entityType.GetDerivedTypes().Any() || entityType.IsOwned())
        {
            AddTranslationErrorDetails("LiteDB bulk commands currently require a non-owned entity without inheritance.");
            return null;
        }
        if (!IsBulkSource(query.ServerQueryExpression))
        {
            AddTranslationErrorDetails("LiteDB bulk commands require a single-collection filter/order/page source; joins and reshaped rows are not update targets.");
            return null;
        }
        var properties = new List<IProperty>();
        var values = new List<Expression>();
        foreach (var setter in setters)
        {
            var target = setter.PropertySelector.Body;
            while (target is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
                target = convert.Operand;
            var property = ResolveBulkProperty(entityType, target, setter.PropertySelector.Parameters[0]);
            if (property == null || property.IsPrimaryKey() || properties.Contains(property)
                || property.FindAnnotation("LiteDB:ComplexPresencePath") != null
                || property.ValueGenerated != ValueGenerated.Never && property.FindAnnotation("LiteDB:ComputedExpression") != null)
            {
                AddTranslationErrorDetails("LiteDB bulk setters must name distinct scalar, non-key, non-computed properties of the target entity.");
                return null;
            }
            var value = setter.ValueExpression is LambdaExpression lambda
                ? TranslateLambdaExpression(source, lambda, preserveType: true)?.Body
                : TranslateExpression(setter.ValueExpression, preserveType: true);
            if (value == null) return null;
            properties.Add(property);
            values.Add(Expression.Convert(value, typeof(object)));
        }
        return new LiteDBBulkExpression(entityType, query.ServerQueryExpression, query.CurrentParameter, properties.ToArray(), values.ToArray());
    }

    private static bool IsBulkSource(Expression source)
        => source is LiteDBTableExpression
            || source is MethodCallExpression call && call.Method.DeclaringType == typeof(Enumerable)
            && call.Method.Name is nameof(Enumerable.Where) or nameof(Enumerable.OrderBy) or nameof(Enumerable.OrderByDescending)
                or nameof(Enumerable.ThenBy) or nameof(Enumerable.ThenByDescending) or nameof(Enumerable.Skip) or nameof(Enumerable.Take)
                or nameof(Enumerable.Distinct) or nameof(Enumerable.Reverse)
            && IsBulkSource(call.Arguments[0]);

    private static IProperty? ResolveBulkProperty(IEntityType entityType, Expression target, ParameterExpression parameter)
    {
        var path = new List<string>();
        while (target != parameter)
        {
            if (target is MemberExpression { Expression: not null } member)
            {
                path.Insert(0, member.Member.Name);
                target = member.Expression;
            }
            else if (target is MethodCallExpression call && call.Method.DeclaringType == typeof(EF)
                && call.Method.Name == nameof(EF.Property) && call.Arguments[1] is ConstantExpression { Value: string name })
            {
                path.Insert(0, name);
                target = call.Arguments[0];
            }
            else return null;
        }
        ITypeBase type = entityType;
        for (var i = 0; i < path.Count - 1; i++)
        {
            var complex = type.FindComplexProperty(path[i]);
            if (complex == null) return null;
            type = complex.ComplexType;
        }
        return path.Count == 0 ? null : type.FindProperty(path[^1]);
    }
}
