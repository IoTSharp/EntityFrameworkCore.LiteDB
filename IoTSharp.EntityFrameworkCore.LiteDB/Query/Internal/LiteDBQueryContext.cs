// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
internal class LiteDBQueryContext : QueryContext
{
    public object ExecuteScalar(LiteDBQueryPlan plan, object[] parameters, string operation)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var value = Store.ExecuteScalar(plan, parameters, operation, _sessionId);
        const string eventName = "IoTSharp.EntityFrameworkCore.LiteDB.AggregateExecuted";
        if (QueryLogger.DiagnosticSource.IsEnabled(eventName)) QueryLogger.DiagnosticSource.Write(eventName, new { Operation = operation, ContextId = Context.ContextId });
        return value;
    }
    public IEnumerable<ValueBuffer> ExecuteNative(LiteDBQueryPlan plan, object[] parameters)
    {
        var rows = Store.ExecuteQuery(plan, parameters, CancellationToken, _sessionId);
        const string eventName = "IoTSharp.EntityFrameworkCore.LiteDB.QueryExecuted";
        if (QueryLogger.DiagnosticSource.IsEnabled(eventName))
            QueryLogger.DiagnosticSource.Write(eventName, new { plan.Predicate, plan.OrderBy, plan.SkipParameter, plan.TakeParameter, RowsRead = rows.Count, ContextId = Context.ContextId });
        return rows.Select(row => new ValueBuffer(row));
    }
    private readonly Guid _sessionId;
    private readonly IDictionary<IEntityType, IEnumerable<ValueBuffer>> _valueBuffersCache
        = new Dictionary<IEntityType, IEnumerable<ValueBuffer>>();

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual IEnumerable<ValueBuffer> GetValueBuffers(IEntityType entityType)
    {
        if (!_valueBuffersCache.TryGetValue(entityType, out var valueBuffers))
        {
            valueBuffers = Store
                .GetTables(entityType, CancellationToken, _sessionId)
                .SelectMany(t => t.Rows.Select(vs => new ValueBuffer(vs)))
                .ToList();

            _valueBuffersCache[entityType] = valueBuffers;
        }

        return valueBuffers;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public LiteDBQueryContext(
        QueryContextDependencies dependencies,
        ILiteDBStore store,
        Guid sessionId)
        : base(dependencies)
    {
        Store = store;
        _sessionId = sessionId;
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual ILiteDBStore Store { get; }
}
