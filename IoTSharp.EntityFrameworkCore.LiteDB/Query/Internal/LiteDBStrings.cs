namespace IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal;

internal static class LiteDBStrings
{
    public const string DefaultIfEmptyAppliedAfterProjection = "DefaultIfEmpty cannot be applied after a client-evaluated projection.";
    public const string DistinctOnSubqueryNotSupported = "Distinct on a projection containing a subquery is not supported.";
    public const string NonComposedGroupByNotSupported = "A non-composed GroupBy operation is not supported.";
    public const string NoQueryStrings = "LiteDB queries do not have a SQL query string.";
    public const string SetOperationsNotAllowedAfterClientEvaluation = "Set operations after client projection are not supported.";

    public static string InvalidDerivedTypeInEntityProjection(object derivedType, object entityType)
        => $"The derived type '{derivedType}' is not valid for entity projection '{entityType}'.";

    public static string UnableToBindMemberToEntityProjection(object memberType, object member, object entityType)
        => $"Unable to bind {memberType} '{member}' to entity projection '{entityType}'.";
}
