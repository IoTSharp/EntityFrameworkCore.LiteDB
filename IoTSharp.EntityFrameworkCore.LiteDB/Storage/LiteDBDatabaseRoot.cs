using System.Collections.Concurrent;
using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

namespace IoTSharp.EntityFrameworkCore.Storage;

/// <summary>Owns shared LiteDB connections. Dispose after all contexts using this root have been disposed.</summary>
public sealed class LiteDBDatabaseRoot : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<LiteDBStore>> _stores = new(StringComparer.Ordinal);
    private bool _disposed;

    internal LiteDBStore GetStore(string connectionString)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _stores.GetOrAdd(connectionString, static name => new Lazy<LiteDBStore>(() => new LiteDBStore(name))).Value;
    }

    /// <summary>Closes all LiteDB connections owned by this root.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var store in _stores.Values)
        {
            if (store.IsValueCreated) store.Value.Dispose();
        }
        _stores.Clear();
    }
}
