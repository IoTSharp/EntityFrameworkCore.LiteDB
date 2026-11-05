# Tag releases to NuGet.org

## 1.1.0 feature release — 2026-11-06

Published `v1.1.0` / NuGet `1.1.0` from exact commit `30cd16022b8ccf295b876cf0a50857a2949566d5`. The [tag workflow](https://github.com/IoTSharp/EntityFrameworkCore.LiteDB/actions/runs/42083398903) passed version validation, build, all **133 tests without skips**, pack and NuGet upload. The release reused the existing repository API key; no duplicate key or upload was created.

- [Public NuGet 1.1.0](https://www.nuget.org/packages/IoTSharp.EntityFrameworkCore.LiteDB/1.1.0)
- [GitHub Release v1.1.0](https://github.com/IoTSharp/EntityFrameworkCore.LiteDB/releases/tag/v1.1.0), with the exact CI-produced nupkg attached.
- Fresh public consumers passed async CRUD, bulk update/delete, optional complex leaf updates, computed values, reopen, rollback and compiled queries. Package repository commit and assembly informational version identify the release SHA; public DLL equals CI DLL byte-for-byte.
- Runtime dependencies remain only LiteDB and Microsoft.EntityFrameworkCore. EF Design is private and does not propagate through the package.

NuGet accepted the upload before its V3 index synchronized. The version page and official V2 download were already readable, so a NuGet.org V2-only fresh consumer was verified first, without retrying the upload. The V3 index subsequently listed 1.1.0, and a second completely fresh consumer restored from **NuGet.org V3 only** and passed the same checks. The public DLL SHA-256 is `b99c646a8e63454fc8a350c9a68622618d2780327014f1181c3a7f97e9ffec27`; the CI nupkg SHA-256 is `e6d4639b95c7d3a6e209176b9427a376b0210928dfed7b010de3074561b49f0b`.

Detailed local receipts/artifacts: `artifacts/release-1.1.0-ci/final-receipt.json`. The release tag stays on the validated source commit; these later documentation changes do not move the tag. Complex collections, precompiled query generation and NativeAOT acceptance remain outside this release's supported contract, as stated in SUPPORT.md.

## First release and reusable workflow

The GitHub Actions workflow in `.github/workflows/build.yml` handles both validation and publication for `IoTSharp.EntityFrameworkCore.LiteDB`.

The first public release of this new package ID is `v1.0.0` / NuGet `1.0.0`. The `v1.0.1` commands below illustrate a subsequent patch release; the old `MaikeBing.EntityFrameworkCore.LiteDB` package has its own version history.

Published on 2026-11-06 (Asia/Shanghai) from commit `d1456132ff1f540b5d769baec29ca2777c68a1dd`. [The tag workflow](https://github.com/IoTSharp/EntityFrameworkCore.LiteDB/actions/runs/42070357278) passed version validation, all 66 provider tests without skips, packing, and NuGet upload on Ubuntu. The [public NuGet package](https://www.nuget.org/packages/IoTSharp.EntityFrameworkCore.LiteDB/1.0.0) and [GitHub Release](https://github.com/IoTSharp/EntityFrameworkCore.LiteDB/releases/tag/v1.0.0) have been independently read back. A fresh NuGet.org consumer restored the package into a new cache and passed async save, generated-key, database-reopen and transaction-rollback checks. Its provider DLL is byte-for-byte identical to the CI artifact, and its assembly informational version includes the release commit.

Publication evidence is retained locally in `artifacts/release-1.0.0-evidence/final-receipt.json`. The release tag remains on the published source commit; later documentation updates do not move the tag or republish the package.

## Trigger and version

- Branch pushes and pull requests restore, build, test, and pack on `ubuntu-latest` without publishing. This provider has no platform-specific code that requires a separate Windows CI job.
- Pushing `v1.0.1` produces package version `1.0.1`. Tags must use lowercase `vMAJOR.MINOR.PATCH`, with no leading zeroes, suffixes, or extra version components. Components are limited to 65534 by .NET assembly version constraints.
- The Ubuntu test job must pass before the publish job runs.
- The publish job downloads the exact `.nupkg` uploaded by the successful test job. It does not rebuild the package and only pushes to `https://api.nuget.org/v3/index.json`.
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

On 2026-11-06 (Asia/Shanghai), the owner account `MysticBoy` created `EntityFrameworkCore-LiteDB-CI-20261106`, scoped to push new packages and versions of exactly `IoTSharp.EntityFrameworkCore.LiteDB`. It has no unlist/relist permission. The key was transferred directly into the repository secret; a separate GitHub API read confirmed the secret exists. The tag workflow subsequently used this secret to publish `1.0.0` successfully.

NuGet.org currently limits newly created API keys to 30 days. This key therefore expires around 2026-12-06 (Asia/Shanghai); check the NuGet API keys page for its exact timestamp and replace the repository secret before it expires. NuGet Trusted Publishing is a possible future alternative to rotating API keys, but this workflow uses the explicitly requested API key.

## CI failure repaired for the first release

Run `42054754435` on commit `305890e1f8b04e638fb098d3bae3e45ec4c15fb0` failed on Ubuntu because `NativeQueryTests.Count_long_count_and_any_use_native_aggregates` collected aggregate diagnostics emitted by other contexts running in parallel. Windows was cancelled by the matrix's former fail-fast behavior.

Native query and aggregate diagnostic payloads now carry `ContextId`, and the observer filters events to its context. The regression `Native_diagnostics_ignore_queries_from_other_contexts` deliberately issues aggregate and row queries through another context while subscribed and verifies that only the observed context contributes events. Parallel test execution remains enabled and exact assertions remain in place.

Local package/build evidence, hosted CI, NuGet publication, and public consumer validation are separate gates. See `artifacts/ci-release-validation/` for local results; those ignored artifacts are not publication receipts.

The diagnostic repair was separately checked against an extraction of committed baseline `305890e`: the new regression failed before the diagnostic payload change, then all 66 tests passed after adding `ContextId`. This temporary baseline was used only for validation and was not a development workspace. An earlier concurrent development snapshot had 105 passes and 4 failures; those unfinished feature changes were excluded from the release commit and are not part of the `1.0.0` acceptance evidence.
