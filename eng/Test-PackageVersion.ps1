$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$deadline = [DateTime]::UtcNow.AddSeconds(10)
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../IoTSharp.EntityFrameworkCore.LiteDB/IoTSharp.EntityFrameworkCore.LiteDB.csproj') -Raw
$defaultVersion = [string]$project.Project.PropertyGroup.Version
$cases = @(
    @{ Ref = 'refs/tags/v1.0.1'; Expected = '1.0.1' },
    @{ Ref = 'refs/tags/v0.0.0'; Expected = '0.0.0' },
    @{ Ref = 'refs/tags/v65534.65534.65534'; Expected = '65534.65534.65534' },
    @{ Ref = 'refs/heads/master'; Expected = $defaultVersion },
    @{ Ref = 'refs/pull/1/merge'; Expected = $defaultVersion },
    @{ Ref = 'refs/tags/1.0.1'; Expected = $null },
    @{ Ref = 'refs/tags/V1.0.1'; Expected = $null },
    @{ Ref = 'refs/tags/v1.0'; Expected = $null },
    @{ Ref = 'refs/tags/v01.0.1'; Expected = $null },
    @{ Ref = 'refs/tags/v1.0.1-preview'; Expected = $null },
    @{ Ref = 'refs/tags/v1.0.1+build'; Expected = $null },
    @{ Ref = 'refs/tags/v1.0.1.0'; Expected = $null },
    @{ Ref = 'refs/tags/v65535.0.0'; Expected = $null },
    @{ Ref = "refs/tags/v1.0.1`n"; Expected = $null }
)
foreach ($case in $cases) {
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Version validation exceeded its 10 second deadline.' }
    $actual = $null
    $failed = $false
    try { $actual = & (Join-Path $PSScriptRoot 'Get-PackageVersion.ps1') -Ref $case.Ref }
    catch { $failed = $true }
    if ($null -eq $case.Expected) {
        if (-not $failed) { throw "Invalid release ref was accepted: $($case.Ref)" }
    }
    elseif ($failed -or $actual -cne $case.Expected) {
        throw "Unexpected package version for $($case.Ref): $actual"
    }
}
Write-Output "PASS: $($cases.Count) package version cases."
