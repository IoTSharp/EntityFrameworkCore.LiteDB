param(
    [Parameter(Mandatory)][ValidateSet('Restore', 'Build', 'Generate', 'Verify', 'Precompile', 'NativeAot')][string] $Mode
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$project = 'validation/CompiledModelProbe/CompiledModelProbe.csproj'
$boundedArguments = switch ($Mode) {
    'Restore' { @('restore', $project) }
    'Build' { @('build', $project, '-c', 'Release', '--no-restore', '-m:1', '-nr:false', '-p:UseSharedCompilation=false') }
    'Generate' { @('ef', 'dbcontext', 'optimize', '--project', $project, '--startup-project', $project, '--context', 'ProbeContext', '--configuration', 'Release', '--output-dir', 'Compiled', '--namespace', 'LiteDBCompiledModelProbe.Compiled', '--no-build') }
    'Verify' { @('run', '--project', $project, '-c', 'Release', '--no-build', '--no-restore') }
    'Precompile' { @('ef', 'dbcontext', 'optimize', '--project', $project, '--startup-project', $project, '--context', 'ProbeContext', '--configuration', 'Release', '--output-dir', '../../artifacts/validation/precompiled-model', '--namespace', 'LiteDBCompiledModelProbe.NativeCompiled', '--precompile-queries', '--nativeaot', '--no-build') }
    'NativeAot' { @('publish', $project, '-c', 'Release', '-r', 'win-x64', '-p:PublishAot=true', '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-o', 'artifacts/validation/compiled-model-aot') }
}
# Invoke-Dotnet owns one process tree, records its PID/start/argv/parent and enforces a 180s deadline.
& (Join-Path $PSScriptRoot 'Invoke-Dotnet.ps1') -TimeoutSeconds 180 -DotnetArguments $boundedArguments
