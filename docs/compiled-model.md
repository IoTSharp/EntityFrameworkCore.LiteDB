# Compiled models, precompiled queries and NativeAOT

Verified on 2026-11-06 with EF Core/Design **10.0.14**, LiteDB **5.0.21** and the existing `dotnet-ef` **10.0.5** installation. The CLI reports that its version is older than the runtime. No tool was installed or upgraded during this verification. That warning remains part of the evidence; results are specific to this tool/runtime combination.

| Gate | Result | Evidence and boundary |
| --- | --- | --- |
| `dotnet ef dbcontext optimize` compiled model generation | **PASS** | The provider now registers its design-time services and the core C# runtime annotation generator. The sample's actual generated source compiled successfully. |
| `UseModel` with the generated runtime model | **PASS** | The standalone probe executed against a real LiteDB file. `ProbeContext.ModelBuildCount` remained **0**, demonstrating that the normal runtime `OnModelCreating` path did not build its model. |
| CRUD, generated integer key, query and file reopen with that model | **PASS** | The probe checked synchronous and asynchronous APIs, released the database root, reopened the file, checked persisted values, updated and deleted the document, and read back the empty result. This establishes the sample's scalar model contract, not every EF model configuration. |
| Query precompilation, including `--nativeaot` model generation | **FAIL: provider code generation gap** | After adding the real mapping prototype/clone contract, generation advanced to query code generation and reported an unsupported constant of type `LiteDBQueryPlan`. Diagnostics identified the sample's `Where(...).ToArrayAsync()`, `Single()` and `AnyAsync()` queries. These plans are embedded as runtime object constants in the current query expression pipeline. |
| NativeAOT publish | **FAIL: environment and unresolved compatibility warnings** | The actual publish returned exit code **1** with `Cross-OS native compilation is not supported`. The run also emitted actual `IL2026`, `IL3050`, `IL2060` and `IL2067` warnings; they were retained rather than suppressed. |
| Execution of a published native application | **NOT_RUN** | No successfully published native executable was available. A JIT probe passing does not establish NativeAOT compatibility. |

The precompilation tool response hit an output limit, so its final process exit code was **UNKNOWN** in that receipt. The explicit query-generation errors establish the provider's current code-generation failure; the receipt does not establish a successful completed command. The NativeAOT publish has a separate, completed exit-1 receipt. Local receipts are under `artifacts/validation/ef10-followup-precompile.json` and `artifacts/validation/ef10-followup-nativeaot-initial.json`; they are validation outputs and are not shipped as source.

## Reproduce the compiled model check

The standalone project is `validation/CompiledModelProbe/CompiledModelProbe.csproj`. It uses the provider project and a private EF Design dependency. Run these steps serially from the repository root with PowerShell 7:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File eng/Invoke-CompiledModelProbe.ps1 -Mode Restore
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File eng/Invoke-CompiledModelProbe.ps1 -Mode Build
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File eng/Invoke-CompiledModelProbe.ps1 -Mode Generate
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File eng/Invoke-CompiledModelProbe.ps1 -Mode Build
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File eng/Invoke-CompiledModelProbe.ps1 -Mode Verify
```

The script uses the existing bounded `eng/Invoke-Dotnet.ps1` runner: each external command has a 180-second deadline and an owned process receipt, and builds use one MSBuild worker with node reuse and shared compilation disabled. The `Generate` command uses `--no-build`, so the preceding build is required. Generated source under the probe's `Compiled/` directory is intentionally ignored and regenerated; it is not checked in. The second build compiles that source and enables the probe's explicit `UseModel` path. Running `Verify` before generation returns **NOT_RUN** instead of silently testing a normally built model.

The successful probe emits JSON including `compiledModel`, `asyncCrud`, `reopen`, `modelBuildCount`, `dynamicCodeSupported` and `nativeAot`. On a normal JIT runtime `nativeAot` is **NOT_RUN**. Its uniquely named temporary database directory is disposed and removed in a `finally` path after checking its absolute cleanup path.

## Reproduce the remaining boundaries

After the compiled-model sequence above, run each experiment separately:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File eng/Invoke-CompiledModelProbe.ps1 -Mode Precompile
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File eng/Invoke-CompiledModelProbe.ps1 -Mode NativeAot
```

`Precompile` writes experimental output to `artifacts/validation/precompiled-model`, with a separate namespace. It does not overwrite the generated model that already passed the JIT probe. `NativeAot` requests `win-x64` and writes to `artifacts/validation/compiled-model-aot`; that request must be assessed against a compatible native build environment before claiming an executable can run.

The next provider task for precompiled queries is to emit reconstructible LiteDB query-plan code, parameter factories and referenced metadata instead of opaque `Constant(LiteDBQueryPlan)` nodes, then rerun the actual EF query precompiler and consume its generated code. Making the type public alone would not implement that contract. NativeAOT additionally requires resolving the provider's dynamic reflection/expression-code-generation and trimming warnings, followed by a successful native publish and execution with real LiteDB persistence. The current cross-OS publish failure is not evidence that LiteDB itself cannot support AOT.
