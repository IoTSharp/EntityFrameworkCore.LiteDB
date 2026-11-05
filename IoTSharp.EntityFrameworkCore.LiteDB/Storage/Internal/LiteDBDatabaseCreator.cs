using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBDatabaseCreator : IDatabaseCreator
{
    private readonly IDatabase _database;
    private readonly ICurrentDbContext _currentContext;
    private readonly IDbContextOptions _options;

    public LiteDBDatabaseCreator(IDatabase database, ICurrentDbContext currentContext, IDbContextOptions options)
    {
        _database = database;
        _currentContext = currentContext;
        _options = options;
    }

    public bool EnsureDeleted() => ((LiteDBDatabase)_database).Store.Clear();

    public Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EnsureDeleted());
    }

    public bool EnsureCreated()
    {
        var created = ((LiteDBDatabase)_database).EnsureDatabaseCreated();
        var core = _options.FindExtension<CoreOptionsExtension>();
        core?.Seeder?.Invoke(_currentContext.Context, created);
        if (core?.Seeder == null && core?.AsyncSeeder != null) throw new InvalidOperationException(CoreStrings.MissingSeeder);
        return created;
    }

    public async Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var created = ((LiteDBDatabase)_database).EnsureDatabaseCreated();
        var core = _options.FindExtension<CoreOptionsExtension>();
        if (core?.AsyncSeeder != null) await core.AsyncSeeder(_currentContext.Context, created, cancellationToken).ConfigureAwait(false);
        else if (core?.Seeder != null) throw new InvalidOperationException(CoreStrings.MissingSeeder);
        return created;
    }

    public bool CanConnect() => true;
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}
