[![License](https://img.shields.io/github/license/maikebing/EntityFrameworkCore.LiteDB.svg)](https://github.com/maikebing/EntityFrameworkCore.LiteDB/blob/master/LICENSE)
[![NuGet](https://img.shields.io/nuget/v/IoTSharp.EntityFrameworkCore.LiteDB.svg)](https://www.nuget.org/packages/IoTSharp.EntityFrameworkCore.LiteDB/)

# IoTSharp.EntityFrameworkCore.LiteDB

`IoTSharp.EntityFrameworkCore.LiteDB` is an EF Core 10 provider that persists EF entities in LiteDB collections. The package targets `net10.0` and is tested with `Microsoft.EntityFrameworkCore 10.0.14` and the latest stable `LiteDB 5.0.21`. Dependencies are restricted to EF Core 10 and LiteDB 5.

The provider implements its own services, store interfaces, query nodes, BSON mapping and transactions against EF Core's provider extension points. It has **no dependency on Microsoft.EntityFrameworkCore.InMemory**, implements no InMemory interfaces, and inherits no InMemory provider classes. Relevant upstream query algorithms and test scenarios are adapted as source references with attribution in [UPSTREAM.md](UPSTREAM.md).

LiteDB executes the supported native numerical filter/order/page plans and Count/LongCount/Any aggregates. Remaining supported compositions operate over rows read from LiteDB; this does not imply complete query pushdown. See [SUPPORT.md](SUPPORT.md) for tested behavior and specific gaps.

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

The public namespace is now `IoTSharp.EntityFrameworkCore`. The project, assembly, package, solution entry, and examples use the `IoTSharp.EntityFrameworkCore.LiteDB` name. A locally packed version is available for validation; the command above applies after the new package has been published.

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

`MaikeBing.EntityFrameworkCore.LiteDB` is deprecated in this repository and does not receive the EF Core 10 implementation. Install `IoTSharp.EntityFrameworkCore.LiteDB`, change `using MaikeBing.EntityFrameworkCore;` to `using IoTSharp.EntityFrameworkCore;`, and keep the existing `UseLiteDB(...)` call. The old NuGet package's online deprecation flag requires a separate owner action, detailed in [docs/nuget-deprecation.md](docs/nuget-deprecation.md).

Back up and export data with the old provider before migration. The new provider's key encoding, BSON layout and collection mapping are not a promise to read legacy provider files in place. The namespace/assembly rename also requires recompilation of consumers.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
