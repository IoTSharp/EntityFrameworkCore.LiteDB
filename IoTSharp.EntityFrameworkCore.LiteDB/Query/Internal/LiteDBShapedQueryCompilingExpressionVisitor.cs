// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal;

using static Expression;

internal partial class LiteDBShapedQueryCompilingExpressionVisitor : ShapedQueryCompilingExpressionVisitor
{
    protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
    {
        if (methodCallExpression.Method.DeclaringType == typeof(Enumerable)
            && methodCallExpression.Arguments.Count == 1
            && methodCallExpression.Method.Name is nameof(Enumerable.Count) or nameof(Enumerable.LongCount) or nameof(Enumerable.Any)
            && LiteDBQueryPlan.TryCreate(methodCallExpression.Arguments[0], out var scalarPlan, scalar: true)
            && scalarPlan.SkipParameter == null && scalarPlan.TakeParameter == null)
        {
            return Convert(Call(Convert(QueryCompilationContext.QueryContextParameter, typeof(LiteDBQueryContext)),
                typeof(LiteDBQueryContext).GetMethod(nameof(LiteDBQueryContext.ExecuteScalar))!,
                Constant(scalarPlan), NewArrayInit(typeof(object), scalarPlan.Parameters), Constant(methodCallExpression.Method.Name)), methodCallExpression.Type);
        }
        if (methodCallExpression.Type == typeof(IEnumerable<ValueBuffer>)
            && LiteDBQueryPlan.TryCreate(methodCallExpression, out var plan))
        {
            return Call(Convert(QueryCompilationContext.QueryContextParameter, typeof(LiteDBQueryContext)),
                typeof(LiteDBQueryContext).GetMethod(nameof(LiteDBQueryContext.ExecuteNative))!,
                Constant(plan), NewArrayInit(typeof(object), plan.Parameters));
        }
        return base.VisitMethodCall(methodCallExpression);
    }
    private readonly Type _contextType;
    private readonly bool _threadSafetyChecksEnabled;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public LiteDBShapedQueryCompilingExpressionVisitor(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        QueryCompilationContext queryCompilationContext)
        : base(dependencies, queryCompilationContext)
    {
        _contextType = queryCompilationContext.ContextType;
        _threadSafetyChecksEnabled = dependencies.CoreSingletonOptions.AreThreadSafetyChecksEnabled;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitExtension(Expression extensionExpression)
    {
        switch (extensionExpression)
        {
            case LiteDBTableExpression liteDBTableExpression:
                return Call(
                    TableMethodInfo,
                    QueryCompilationContext.QueryContextParameter,
                    Constant(liteDBTableExpression.EntityType));
        }

        return base.VisitExtension(extensionExpression);
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        var liteDBQueryExpression = (LiteDBQueryExpression)shapedQueryExpression.QueryExpression;
        liteDBQueryExpression.ApplyProjection();

        var shaperExpression = new ShaperExpressionProcessingExpressionVisitor(
                this, liteDBQueryExpression, QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.TrackAll)
            .ProcessShaper(shapedQueryExpression.ShaperExpression);
        var innerEnumerable = Visit(liteDBQueryExpression.ServerQueryExpression);

        return New(
            typeof(QueryingEnumerable<>).MakeGenericType(shaperExpression.ReturnType).GetConstructors()[0],
            QueryCompilationContext.QueryContextParameter,
            innerEnumerable,
            Constant(shaperExpression.Compile()),
            Constant(_contextType),
            Constant(
                QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.NoTrackingWithIdentityResolution),
            Constant(_threadSafetyChecksEnabled));
    }

    private static readonly MethodInfo TableMethodInfo
        = typeof(LiteDBShapedQueryCompilingExpressionVisitor).GetTypeInfo().GetDeclaredMethod(nameof(Table))!;

    private static IEnumerable<ValueBuffer> Table(
        QueryContext queryContext,
        IEntityType entityType)
        => ((LiteDBQueryContext)queryContext).GetValueBuffers(entityType);
}
