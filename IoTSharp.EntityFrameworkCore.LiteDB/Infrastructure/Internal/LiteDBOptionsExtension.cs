using IoTSharp.EntityFrameworkCore.Storage;
using IoTSharp.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Infrastructure.Internal;

internal sealed class LiteDBOptionsExtension : IDbContextOptionsExtension
{
    public string StoreName { get; private set; }
    public LiteDBDatabaseRoot DatabaseRoot { get; private set; }
    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public LiteDBOptionsExtension WithStoreName(string name)
        => new() { StoreName = name, DatabaseRoot = DatabaseRoot };

    public LiteDBOptionsExtension WithDatabaseRoot(LiteDBDatabaseRoot root)
        => new() { StoreName = StoreName, DatabaseRoot = root };

    public void ApplyServices(IServiceCollection services) => services.AddEntityFrameworkLiteDBDatabase();

    public void Validate(IDbContextOptions options)
        => ArgumentException.ThrowIfNullOrWhiteSpace(StoreName);

    private sealed class ExtensionInfo(LiteDBOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => true;
        // Connection strings may contain passwords; never include them in EF log fragments.
        public override string LogFragment => "UsingLiteDB ";
        public override int GetServiceProviderHashCode() => 0;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["LiteDB:Provider"] = "1";
    }
}
