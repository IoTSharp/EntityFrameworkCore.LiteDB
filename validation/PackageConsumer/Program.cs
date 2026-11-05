using IoTSharp.EntityFrameworkCore;
using IoTSharp.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;

var directory = Path.Combine(Path.GetTempPath(), "iotsharp-package-followup-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var file = Path.Combine(directory, "consumer.db");
    var id = 0;
    using (var root = new LiteDBDatabaseRoot())
    using (var db = new ConsumerContext(file, root))
    {
        db.Add(new Note { Code = "one", Amount = 2.5m });
        Check(await db.SaveChangesAsync() == 1, "async insert");
        id = db.Notes.Single().Id;
        Check(id > 0 && db.Notes.Single().Total == 5m, "generated values");
        Check(await db.Notes.ExecuteUpdateAsync(s => s.SetProperty(n => n.Amount, n => n.Amount + 1m)) == 1, "bulk count");
        db.ChangeTracker.Clear();
        Check(db.Notes.Single().Total == 7m, "bulk computed value");
        Check(await db.Notes.ExecuteUpdateAsync(s => s.SetProperty(n => n.Address!.City, "Urumqi")) == 1, "complex bulk");
    }
    using (var root = new LiteDBDatabaseRoot())
    using (var db = new ConsumerContext(file, root))
    {
        var row = await db.Notes.SingleAsync();
        Check(row.Id == id && row.Amount == 3.5m && row.Address?.City == "Urumqi", "file reopen");
        using (var transaction = db.Database.BeginTransaction())
        {
            db.Notes.ExecuteUpdate(s => s.SetProperty(n => n.Amount, 100m));
            transaction.Rollback();
        }
        db.ChangeTracker.Clear();
        var compiled = EF.CompileQuery((ConsumerContext context, decimal minimum) => context.Notes.Count(n => n.Amount > minimum));
        Check(compiled(db, 3m) == 1, "compiled query / rollback");
        Check(await db.Notes.ExecuteDeleteAsync() == 1 && !await db.Notes.AnyAsync(), "async bulk delete");
    }
    var location = typeof(LiteDBDatabaseRoot).Assembly.Location;
    var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
    Check(cache != null, "isolated package cache");
    var packageVersion = Environment.GetEnvironmentVariable("RELEASE_PACKAGE_VERSION") ?? "1.1.0";
    var publicConsumer = Environment.GetEnvironmentVariable("RELEASE_PUBLIC_CONSUMER") == "1";
    var packageFile = Environment.GetEnvironmentVariable("RELEASE_PACKAGE_ARCHIVE")
        ?? Path.GetFullPath($"artifacts/packages/IoTSharp.EntityFrameworkCore.LiteDB.{packageVersion}.nupkg");
    var cachedFile = Path.Combine(cache!, "iotsharp.entityframeworkcore.litedb", packageVersion, $"iotsharp.entityframeworkcore.litedb.{packageVersion}.nupkg");
    string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    var cachedAssembly = Path.Combine(cache!, "iotsharp.entityframeworkcore.litedb", packageVersion, "lib", "net10.0", "IoTSharp.EntityFrameworkCore.LiteDB.dll");
    Check(Hash(location) == Hash(cachedAssembly), "isolated package assembly");
    // NuGet can add repository signatures to the published ZIP; DLL identity is the public contract.
    if (!publicConsumer) Check(Hash(packageFile) == Hash(cachedFile), "exact packed archive");
    using var archive = System.IO.Compression.ZipFile.OpenRead(packageFile);
    using var providerDll = archive.GetEntry("lib/net10.0/IoTSharp.EntityFrameworkCore.LiteDB.dll")!.Open();
    Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(providerDll)) == Hash(cachedAssembly), "CI archive DLL equals consumed DLL");
    var nuspec = archive.Entries.Single(e => e.Name.EndsWith(".nuspec", StringComparison.Ordinal));
    using var metadata = nuspec.Open();
    var document = System.Xml.Linq.XDocument.Load(metadata);
    var dependencies = document.Descendants().Where(e => e.Name.LocalName == "dependency").Select(e => (string)e.Attribute("id")!).Order().ToArray();
    Check(dependencies.SequenceEqual(new[] { "LiteDB", "Microsoft.EntityFrameworkCore" }), "only two runtime dependencies");
    Check(document.Descendants().Single(e => e.Name.LocalName == "version").Value == packageVersion, "package version");
    var commit = (string?)document.Descendants().Single(e => e.Name.LocalName == "repository").Attribute("commit");
    var expectedCommit = Environment.GetEnvironmentVariable("RELEASE_COMMIT");
    var informationalVersion = typeof(LiteDBDatabaseRoot).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
    if (expectedCommit != null) Check(commit == expectedCommit && informationalVersion.Contains(expectedCommit, StringComparison.OrdinalIgnoreCase), "release source commit");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { packageConsumer = "PASS", publicConsumer, packageVersion, asyncCrud = "PASS", bulk = "PASS", complex = "PASS", generated = "PASS", reopen = "PASS", rollback = "PASS", compiledQuery = "PASS", dllMatch = "PASS", packageSha256 = Hash(packageFile), dependencies, commit, informationalVersion, assembly = location, cachedAssembly }));
}
finally
{
    // The path is freshly generated by this invocation and all LiteDB roots have been disposed.
    if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
}

static void Check(bool condition, string gate)
{
    if (!condition) throw new InvalidOperationException("Consumer failed: " + gate);
}

public sealed class ConsumerContext(string file, LiteDBDatabaseRoot root) : DbContext
{
    public DbSet<Note> Notes => Set<Note>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseLiteDB(file, root);
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Note>().HasIndex(n => n.Code).IsUnique();
        model.Entity<Note>().ComplexProperty(n => n.Address).IsRequired(false);
        model.Entity<Note>().Property(n => n.Total).HasLiteDBComputedExpression("$.Amount * 2");
    }
}
public sealed class Note
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Total { get; set; }
    public AddressValue? Address { get; set; }
}
public sealed class AddressValue { public string? City { get; set; } }
