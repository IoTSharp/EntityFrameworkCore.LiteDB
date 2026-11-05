param(
    [Parameter(Mandatory)][string] $Ref
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }

if ($Ref.StartsWith('refs/tags/', [StringComparison]::Ordinal)) {
    if ($Ref -cnotmatch '\Arefs/tags/v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
        throw 'Release tags must use vMAJOR.MINOR.PATCH, for example v1.0.1, without leading zeroes or a suffix.'
    }
    $version = $Ref.Substring('refs/tags/v'.Length)
    foreach ($component in $version.Split('.')) {
        if ([decimal]$component -gt 65534) {
            throw 'Release version components must be at most 65534 for .NET assembly versions.'
        }
    }
    return $version
}

[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../IoTSharp.EntityFrameworkCore.LiteDB/IoTSharp.EntityFrameworkCore.LiteDB.csproj') -Raw
return [string]$project.Project.PropertyGroup.Version
