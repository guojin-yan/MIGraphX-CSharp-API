[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
Push-Location $root
try {
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
    }
    Invoke-DotNet -Arguments @('tool', 'restore')
    Invoke-DotNet -Arguments @('tool', 'run', 'docfx', '.\docfx.json', '--warningsAsErrors')
    foreach ($path in @(
        'artifacts\docs\index.html',
        'artifacts\docs\api\index.html',
        'artifacts\docs\api\JYPPX.ROCm.MIGraphXSharp.MIGraphXBuildInfo.html'
    )) {
        if (-not (Test-Path -LiteralPath $path)) { throw "DocFX output is missing $path." }
    }
}
finally {
    Pop-Location
}
