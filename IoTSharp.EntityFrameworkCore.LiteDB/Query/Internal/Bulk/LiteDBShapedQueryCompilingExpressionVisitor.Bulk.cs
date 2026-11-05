namespace IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal;

internal partial class LiteDBShapedQueryCompilingExpressionVisitor
{
    private Expression CompileBulk(LiteDBBulkExpression bulk)
    {
        var context = Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(LiteDBQueryContext));
        var command = Expression.Call(context, typeof(LiteDBQueryContext).GetMethod(nameof(LiteDBQueryContext.ExecuteBulk))!,
            Expression.Constant(bulk.EntityType),
            Expression.Lambda<Func<IEnumerable<ValueBuffer>>>(Visit(bulk.Source)),
            Expression.Constant(bulk.Properties),
            Expression.Lambda<Func<ValueBuffer, object[]>>(Expression.NewArrayInit(typeof(object), bulk.Values.Select(v => Visit(v))), bulk.Row));
        return QueryCompilationContext.IsAsync
            ? Expression.Call(typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(int)), command)
            : command;
    }
}
