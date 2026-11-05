using System.Runtime.CompilerServices;
using IoTSharp.EntityFrameworkCore;
using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LiteDBCompiledModelProbe;

public static class Program
{
    public static Task<int> Main()
    {
#if !COMPILED_MODEL
        Console.Error.WriteLine("NOT_RUN: Generate the compiled model with eng/Invoke-CompiledModelProbe.ps1 -Mode Generate, then rebuild.");
        return Task.FromResult(2);
#else
        return Verify();
#endif
    }

#if COMPILED_MODEL
    private static async Task<int> Verify()
    {
        var directory = Path.Combine(Path.GetTempPath(), "iotsharp-compiled-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "probe.db");
        try
        {
            using (var root = new LiteDBDatabaseRoot())
            {
                using var write = new ProbeContext(path, root, useCompiledModel: true);
                write.Notes.Add(new ProbeNote { Text = "compiled model", Amount = 2.5m });
                await write.SaveChangesAsync();
                var threshold = 2m;
                var rows = await write.Notes.AsNoTracking().Where(n => n.Amount > threshold).ToArrayAsync();
                if (rows.Length != 1 || rows[0].Id <= 0) throw new InvalidOperationException("Compiled model write/query failed.");
            }
            using (var root = new LiteDBDatabaseRoot())
            {
                using var reopened = new ProbeContext(path, root, useCompiledModel: true);
                var row = reopened.Notes.Single();
                if (row.Text != "compiled model" || row.Amount != 2.5m) throw new InvalidOperationException("Reopen readback failed.");
                row.Text = "updated";
                reopened.SaveChanges();
                reopened.Notes.Remove(row);
                await reopened.SaveChangesAsync();
                if (await reopened.Notes.AnyAsync()) throw new InvalidOperationException("Delete readback failed.");
            }
            if (ProbeContext.ModelBuildCount != 0) throw new InvalidOperationException("OnModelCreating ran despite UseModel.");
            var dynamicCode = RuntimeFeature.IsDynamicCodeSupported ? "true" : "false";
            var nativeAot = RuntimeFeature.IsDynamicCodeSupported ? "NOT_RUN" : "PASS";
            Console.WriteLine($"{{\"compiledModel\":\"PASS\",\"asyncCrud\":\"PASS\",\"reopen\":\"PASS\",\"modelBuildCount\":{ProbeContext.ModelBuildCount},\"dynamicCodeSupported\":{dynamicCode},\"nativeAot\":\"{nativeAot}\"}}");
            return 0;
        }
        finally
        {
            // Only this fresh, uniquely named probe directory belongs to this invocation.
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("iotsharp-compiled-model-", StringComparison.Ordinal))
                throw new InvalidOperationException("Probe cleanup path escaped its owned temporary directory.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }
#endif
}

public sealed class ProbeContext(string path, LiteDBDatabaseRoot? root = null, bool useCompiledModel = false) : DbContext
{
    public static int ModelBuildCount { get; private set; }
    public DbSet<ProbeNote> Notes => Set<ProbeNote>();
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        _ = useCompiledModel;
        if (root == null) options.UseLiteDB(path);
        else options.UseLiteDB(path, root);
#if COMPILED_MODEL
        if (useCompiledModel) options.UseModel(Compiled.ProbeContextModel.Instance);
#endif
    }
    protected override void OnModelCreating(ModelBuilder model)
    {
        ModelBuildCount++;
        model.Entity<ProbeNote>().Property(n => n.Text).IsRequired();
    }
}

public sealed class ProbeNote
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public decimal Amount { get; set; }
}

public sealed class ProbeContextFactory : IDesignTimeDbContextFactory<ProbeContext>
{
    public ProbeContext CreateDbContext(string[] args) => new("compiled-model-design.db");
}
