# Upstream references and test provenance

The query expression algorithms and small shared helpers in `IoTSharp.EntityFrameworkCore.LiteDB/Query/Internal` were adapted from the **tagged EF Core v10.0.14 source**, under the MIT license. The upstream license is preserved in `licenses/EFCore-MIT.txt` and packed with this provider. These are source adaptations owned by this provider; there is no runtime or compile dependency on `Microsoft.EntityFrameworkCore.InMemory`, no `IInMemory*` implementation, and no inheritance from an InMemory provider class.

- Source: https://github.com/dotnet/efcore/tree/v10.0.14/src/EFCore.InMemory/Query/Internal
- Shared helpers: https://github.com/dotnet/efcore/tree/v10.0.14/src/Shared
- EF provider contract: https://learn.microsoft.com/ef/core/providers/writing-a-provider

The adapted query infrastructure handles EF expression shapes, identity resolution, navigation fixup, and async enumeration. LiteDB-specific BSON serialization, key encoding, indexes, collection lifecycle, transactions, native query plans, and persistence are implemented separately in this repository. Numerical filters, primary ordering and paging have a native LiteDB execution path. Query compositions outside that subset currently read LiteDB rows and evaluate their remaining operators in the provider; they do not imply native LiteDB translation or index use.

## Test lineage

`QuerySpecificationTests` adapts these scenarios from [NorthwindWhereQueryTestBase.cs](https://github.com/dotnet/efcore/blob/v10.0.14/test/EFCore.Specification.Tests/Query/NorthwindWhereQueryTestBase.cs):

| Upstream method | Local adaptation |
| --- | --- |
| `Where_simple` | Same predicate pattern over LiteDB-backed customers, sync and async |
| `Where_simple_closure_via_query_cache` | Reuses a cached query after changing its captured city |
| `Where_indexer_closure` | Captured array access becomes a query parameter |
| `Where_dictionary_key_access_closure` | Captured dictionary access becomes a query parameter |

`StorageSpecificationTests` adapts duplicate insert, duplicate delete, concurrency resolution and Reload scenarios from [OptimisticConcurrencyTestBase.cs](https://github.com/dotnet/efcore/blob/v10.0.14/test/EFCore.Specification.Tests/OptimisticConcurrencyTestBase.cs). Models and fixture plumbing are reduced to a small embedded database; assertions still check the provider's public behavior and exception entries.

Additional tests are specific to LiteDB: closing and reopening the file; direct BSON readback; explicit and composite keys; exact scalar round trips; value converters; primitive collections; owned and many-to-many mappings; EF seeding delegates; native query diagnostics; and real commit/rollback behavior. They are not copies of the implementation. Tests record supported outcomes, not blanket conformance to the complete upstream provider specification suite.

For every future upstream upgrade, keep the source tag, preserve license notices, adapt only relevant tests, and rerun LiteDB-specific persistence and transaction tests. Passing upstream-style query cases does not establish native query pushdown.

## EF Core 10 follow-up implementation

The design-service registration pattern references the tagged [InMemoryDesignTimeServices.cs](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.InMemory/Design/Internal/InMemoryDesignTimeServices.cs), and the immutable type-mapping `Default` prototype/`Clone` code-generation contract references [InMemoryTypeMapping.cs](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore.InMemory/Storage/Internal/InMemoryTypeMapping.cs). The provider registers its own services and mapping; it does not inherit or instantiate either upstream provider class. EF Design 10.0.14 is a private design-time compilation dependency and is not a package runtime dependency.

Optional complex materialization was checked against [StructuralTypeMaterializerSource.cs](https://github.com/dotnet/efcore/blob/v10.0.14/src/EFCore/Query/Internal/StructuralTypeMaterializerSource.cs), especially `HandleNullableComplexTypeMaterialization`. LiteDB persists explicit presence flags to distinguish a null complex value from a present object with all-null/default leaves; its shaper replaces the upstream all-null heuristic with those flags. Provider tracking and design hooks intentionally use EF internal contracts and require renewed verification at every EF upgrade.

BulkOperationTests, BulkComplexPropertyTests, BulkOptionalComplexPropertyTests, BulkGeneratedValueTests, ComplexPropertyTests, GeneratedValueTests, NativeExtendedQueryTests and MigrationArchiveTests are provider-specific tests over real LiteDB files, transactions and reopen readback. Bulk tests exercise EF10's public setters/count/Tracker contract and use LiteDB UpdateMany/DeleteMany, without substituting InMemory execution. Native tests distinguish actual BSON execution from CLR fallback with context-correlated diagnostics; they include nulls, culture, decimal precision, date ticks, offsets and integer extremes.

The legacy archive fixture follows this repository's old `6826e3a` LiteDBTable.Create use of `BsonMapper.Global.ToDocument(entity)`. It is a layout fixture, not evidence that the old EF6 assembly runs on .NET 10 or that historical files can be upgraded in place. Compiled-model and AOT evidence is kept separately in [docs/compiled-model.md](docs/compiled-model.md).
