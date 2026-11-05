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
