using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal interface ILiteDBStore
{
    bool EnsureCreated(IUpdateAdapterFactory updateAdapterFactory, IModel designModel, IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger);
    bool Clear();
    IReadOnlyList<LiteDBTableSnapshot> GetTables(IEntityType entityType, CancellationToken cancellationToken = default, Guid sessionId = default);
    LiteDBIntegerValueGenerator<TProperty> GetIntegerValueGenerator<TProperty>(IProperty property);
    int ExecuteTransaction(IList<IUpdateEntry> entries, IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger, Guid sessionId = default);
    void BeginTransaction(Guid sessionId);
    void CommitTransaction(Guid sessionId);
    void RollbackTransaction(Guid sessionId);
    IReadOnlyList<object[]> ExecuteQuery(IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.LiteDBQueryPlan plan, object[] parameters, CancellationToken cancellationToken, Guid sessionId);
    object ExecuteScalar(IoTSharp.EntityFrameworkCore.LiteDB.Query.Internal.LiteDBQueryPlan plan, object[] parameters, string operation, Guid sessionId);
    int ExecuteBulk(IEntityType entityType, Func<IEnumerable<ValueBuffer>> source, IProperty[] properties, Func<ValueBuffer, object[]> values, CancellationToken cancellationToken, Guid sessionId);
}
