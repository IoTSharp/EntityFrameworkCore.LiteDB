namespace IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal;

/// <summary>A non-query command over untracked LiteDB value buffers.</summary>
internal sealed class LiteDBBulkExpression(
    IEntityType entityType,
    Expression source,
    ParameterExpression row,
    IProperty[] properties,
    Expression[] values) : Expression
{
    public IEntityType EntityType { get; } = entityType;
    public Expression Source { get; } = source;
    public ParameterExpression Row { get; } = row;
    public IProperty[] Properties { get; } = properties;
    public Expression[] Values { get; } = values;
    public override ExpressionType NodeType => ExpressionType.Extension;
    public override Type Type => typeof(int);
    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;
}
