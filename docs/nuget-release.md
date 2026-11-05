# Tag releases to NuGet.org

The GitHub Actions workflow in `.github/workflows/build.yml` handles both validation and publication for `IoTSharp.EntityFrameworkCore.LiteDB`.

The first public release of this new package ID is `v1.0.0` / NuGet `1.0.0`. The `v1.0.1` commands below illustrate a subsequent patch release; the old `MaikeBing.EntityFrameworkCore.LiteDB` package has its own version history.

## Trigger and version

- Branch pushes and pull requests restore, build, test, and pack on Ubuntu and Windows without publishing.
- Pushing `v1.0.1` produces package version `1.0.1`. Tags must use lowercase `vMAJOR.MINOR.PATCH`, with no leading zeroes, suffixes, or extra version components. Components are limited to 65534 by .NET assembly version constraints.
- Both operating systems must pass before the publish job runs. A failing matrix job does not cancel the other operating system, so both sets of results remain available.
- The publish job downloads the exact `.nupkg` uploaded by the successful Ubuntu job. It does not rebuild the package and only pushes to `https://api.nuget.org/v3/index.json`.
- `Version` is passed to both build and pack. The SDK derives assembly and file versions from it; they are no longer pinned at `1.0.0.0`.
- Concurrent runs for the same ref are serialized. Published versions are immutable; duplicate uploads fail rather than being treated as successful replacements.

After the workflow changes have been committed and pushed and the intended commit passes CI, create and push an annotated release tag:

```powershell
git tag -a v1.0.1 -m "Release 1.0.1"
git push origin v1.0.1
```

Do not move a published tag. If an upload times out or a workflow rerun encounters an existing version, first inspect the NuGet package and the original Actions run rather than blindly repeating the write. After publishing, verify the public package ID, version, dependencies, README, and repository commit and restore that version in a fresh consumer.

## Credential

The publish job reads only the repository Actions secret `NUGET_API_KEY`. Never put its value in workflow YAML, Git, or logs.

On 2026-11-06 (Asia/Shanghai), the owner account `MysticBoy` created `EntityFrameworkCore-LiteDB-CI-20261106`, scoped to push new packages and versions of exactly `IoTSharp.EntityFrameworkCore.LiteDB`. It has no unlist/relist permission. The key was transferred directly into the repository secret; a separate GitHub API read confirmed the secret exists. This confirms configuration, not an actual successful package upload.

NuGet.org currently limits newly created API keys to 30 days. This key therefore expires around 2026-12-06 (Asia/Shanghai); check the NuGet API keys page for its exact timestamp and replace the repository secret before it expires. NuGet Trusted Publishing is a possible future alternative to rotating API keys, but this workflow uses the explicitly requested API key.

## CI failure fixed in the working tree

Run `42054754435` on commit `305890e1f8b04e638fb098d3bae3e45ec4c15fb0` failed on Ubuntu because `NativeQueryTests.Count_long_count_and_any_use_native_aggregates` collected aggregate diagnostics emitted by other contexts running in parallel. Windows was cancelled by the matrix's former fail-fast behavior.

Native query and aggregate diagnostic payloads now carry `ContextId`, and the observer filters events to its context. The regression `Native_diagnostics_ignore_queries_from_other_contexts` deliberately issues aggregate and row queries through another context while subscribed and verifies that only the observed context contributes events. Parallel test execution remains enabled and exact assertions remain in place.

Local package/build evidence, hosted CI, NuGet publication, and public consumer validation are separate gates. See `artifacts/ci-release-validation/` for local results; those ignored artifacts are not publication receipts.

The diagnostic repair was separately checked against an extraction of committed baseline `305890e`: the new regression failed before the diagnostic payload change, then all 66 tests passed after adding `ContextId`. This temporary baseline was used only for validation and was not a development workspace. The concurrently changing main working tree had 105 passes and 4 failures in its latest full run (optional complex null round trips and extended native query diagnostics), so that result does not establish release readiness for those additional changes.
