using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBDatabase : Database
{
    private readonly ILiteDBStore _store;
    private readonly Guid _sessionId;
    private readonly IUpdateAdapterFactory _updateAdapterFactory;
    private readonly IDesignTimeModel _designTimeModel;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Update> _updateLogger;

    public LiteDBDatabase(
        DatabaseDependencies dependencies,
        ILiteDBStoreProvider storeProvider,
        IDesignTimeModel designTimeModel,
        IUpdateAdapterFactory updateAdapterFactory,
        IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger)
        : base(dependencies)
    {
        _store = storeProvider.Store;
        _sessionId = storeProvider.SessionId;
        _designTimeModel = designTimeModel;
        _updateAdapterFactory = updateAdapterFactory;
        _updateLogger = updateLogger;
    }

    public override int SaveChanges(IList<IUpdateEntry> entries)
        => _store.ExecuteTransaction(entries, _updateLogger, _sessionId);

    public override Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = default)
        => cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<int>(cancellationToken)
            : Task.FromResult(_store.ExecuteTransaction(entries, _updateLogger, _sessionId));

    public bool EnsureDatabaseCreated()
        => _store.EnsureCreated(_updateAdapterFactory, _designTimeModel.Model, _updateLogger);

    public ILiteDBStore Store => _store;
}
