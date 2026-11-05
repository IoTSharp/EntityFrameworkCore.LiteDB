using IoTSharp.EntityFrameworkCore.LiteDB.Infrastructure.Internal;
using IoTSharp.EntityFrameworkCore.Storage;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBStoreProvider : ILiteDBStoreProvider
{
    public LiteDBStoreProvider(IDbContextOptions options, LiteDBStoreCache cache)
    {
        var extension = options.Extensions.OfType<LiteDBOptionsExtension>().Single();
        Store = (extension.DatabaseRoot ?? cache.Root).GetStore(extension.StoreName);
    }

    public ILiteDBStore Store { get; }
    public Guid SessionId { get; } = Guid.NewGuid();
}

internal sealed class LiteDBStoreCache : IDisposable
{
    public LiteDBDatabaseRoot Root { get; } = new();
    public void Dispose() => Root.Dispose();
}
