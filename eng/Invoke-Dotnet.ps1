param(
    [int] $TimeoutSeconds = 180,
    [Parameter(Mandatory)][string[]] $DotnetArguments
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
if ($DotnetArguments.Count -gt 100) { throw 'Too many command arguments.' }
$repo = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $repo 'artifacts/validation'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$name = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff')
$info = [Diagnostics.ProcessStartInfo]::new('C:\Program Files\dotnet\dotnet.exe')
$info.WorkingDirectory = $repo
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.Environment['MSBUILDDISABLENODEREUSE'] = '1'
$info.Environment['DOTNET_CLI_USE_MSBUILD_SERVER'] = '0'
foreach ($argument in $DotnetArguments) { $info.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::new()
$process.StartInfo = $info
try {
    [void]$process.Start()
    $receipt = @{ pid = $process.Id; parentPid = $PID; startedUtc = $process.StartTime.ToUniversalTime().ToString('o'); program = $info.FileName; arguments = $DotnetArguments; timeoutSeconds = $TimeoutSeconds }
    $receipt | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $logDir "$name.process.json")
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw "dotnet exceeded its $TimeoutSeconds second deadline (PID $($process.Id))."
    }
    $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $logDir "$name.log"), $output)
    $output -split '\r?\n' | Select-Object -Last 35
    Write-Output "ExitCode=$($process.ExitCode); Log=artifacts/validation/$name.log"
    exit $process.ExitCode
}
finally {
    if ($process.Id -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    $process.Dispose()
}
