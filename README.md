[![License](https://img.shields.io/github/license/maikebing/EntityFrameworkCore.LiteDB.svg)](https://github.com/maikebing/EntityFrameworkCore.LiteDB/blob/master/LICENSE)
[![NuGet](https://img.shields.io/nuget/v/IoTSharp.EntityFrameworkCore.LiteDB.svg)](https://www.nuget.org/packages/IoTSharp.EntityFrameworkCore.LiteDB/)

# IoTSharp.EntityFrameworkCore.LiteDB

`IoTSharp.EntityFrameworkCore.LiteDB` is an EF Core 10 provider that persists EF entities in LiteDB collections. The package targets `net10.0` and is tested with `Microsoft.EntityFrameworkCore 10.0.14` and the latest stable `LiteDB 5.0.21`. Dependencies are restricted to EF Core 10 and LiteDB 5.

The provider implements its own services, store interfaces, query nodes, BSON mapping and transactions against EF Core's provider extension points. It has **no dependency on Microsoft.EntityFrameworkCore.InMemory**, implements no InMemory interfaces, and inherits no InMemory provider classes. Relevant upstream query algorithms and test scenarios are adapted as source references with attribution in [UPSTREAM.md](UPSTREAM.md).

LiteDB executes supported numerical/date-tick filters, mixed-direction ThenBy, paging, Count/LongCount/Any and exact numeric aggregates. Remaining supported compositions preserve CLR semantics over LiteDB rows; see [SUPPORT.md](SUPPORT.md) for execution and indexing boundaries.

EF10 ExecuteUpdate/ExecuteDelete now use LiteDB bulk writes inside atomic transactions, with affected-row counts and no ChangeTracker refresh. Required, optional and nested ComplexProperty values persist as nested BSON, including null presence, full projections and complex leaf updates. The ToJson container-name mapping is a BSON alias; it does not imply relational JSON-column semantics. Complex collections remain an explicit provider gap.

Configure LiteDB expressions for defaults and computed values using the provider's public extensions:

```csharp
modelBuilder.Entity<Line>().Property(x => x.CreatedLabel)
    .HasLiteDBDefaultExpression("'new'");
modelBuilder.Entity<Line>().Property(x => x.Total)
    .HasLiteDBComputedExpression("$.Price * $.Quantity");
```

Generated expressions run through LiteDB, return values to tracked entities, and participate in SaveChanges/Bulk rollback. They read ordinary fields; generated-on-generated dependencies and key generation expressions are rejected. SQL expressions and automatic rowversion remain unsupported.

Actual compiled-model generation and UseModel CRUD/reopen have passed; query precompilation and NativeAOT have separate failing/unrun gates documented in [docs/compiled-model.md](docs/compiled-model.md). For explicit legacy data export and mapped atomic import, use [LiteDBDataArchive](docs/migration-archive.md); no automatic in-place or binary compatibility is promised.

## Install

```powershell
Install-Package IoTSharp.EntityFrameworkCore.LiteDB
```

```xml
<PackageReference Include="IoTSharp.EntityFrameworkCore.LiteDB" Version="1.0.0" />
```

Configure a context with a LiteDB connection string or file path:

```csharp
using IoTSharp.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public sealed class AppContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseLiteDB("app.db");
}
```

The public namespace is now `IoTSharp.EntityFrameworkCore`. The project, assembly, package, solution entry, and examples use the `IoTSharp.EntityFrameworkCore.LiteDB` name. The first public release, [1.0.0](https://www.nuget.org/packages/IoTSharp.EntityFrameworkCore.LiteDB/1.0.0), is the previously published baseline. The follow-up capabilities described above are in the current source and verified local package; they require a later package release and are not claimed to be present in that already published version.

## Verification

The solution contains `IoTSharp.EntityFrameworkCore.LiteDB.Tests`. Tests adapt identified cases from the tagged EF provider specification suite and add LiteDB-specific reopen/readback, BSON precision, concurrency, atomic transactions, navigation/collection mapping, native query execution diagnostics, seeding, and ASP.NET Core Identity integration. Unsupported operations have explicit failing-contract tests rather than ignored warnings or skipped successes.

Run them with:

```powershell
dotnet test .\IoTSharp.EntityFrameworkCore.LiteDB.Tests\IoTSharp.EntityFrameworkCore.LiteDB.Tests.csproj
```

The detailed capability boundary is in [SUPPORT.md](SUPPORT.md). Unsupported relational APIs must stay marked as unsupported until an implementation and a focused test exist.

## Releases to NuGet.org

Push a tag such as `v1.0.1` to publish package version `1.0.1`. The workflow validates the tag, builds and tests on Ubuntu, and uploads the exact tested package only after validation succeeds. Branch and pull request builds validate and pack without publishing.

```powershell
git tag -a v1.0.1 -m "Release 1.0.1"
git push origin v1.0.1
```

Create the tag on a committed revision containing the release workflow and all intended changes. See [docs/nuget-release.md](docs/nuget-release.md) for credential setup, version rules, and publication checks.

## Migration from the former package

`MaikeBing.EntityFrameworkCore.LiteDB` is deprecated on NuGet.org, where both historical versions recommend `IoTSharp.EntityFrameworkCore.LiteDB`. Install the new package, change `using MaikeBing.EntityFrameworkCore;` to `using IoTSharp.EntityFrameworkCore;`, and keep the existing `UseLiteDB(...)` call. The verified online settings are recorded in [docs/nuget-deprecation.md](docs/nuget-deprecation.md).

Back up and export data with the old provider before migration. The new provider's key encoding, BSON layout and collection mapping are not a promise to read legacy provider files in place. The namespace/assembly rename also requires recompilation of consumers.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
