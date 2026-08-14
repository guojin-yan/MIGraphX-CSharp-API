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
    & (Join-Path $PSScriptRoot 'verify-m3-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m4-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m5-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m6-coverage.ps1') | Out-Host
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
    }
    Invoke-DotNet -Arguments @('tool', 'restore')
    Invoke-DotNet -Arguments @('tool', 'run', 'docfx', (Join-Path $root 'docfx.json'), '--warningsAsErrors')
    foreach ($relativePath in @(
        'artifacts/docs/index.html',
        'artifacts/docs/api/index.html',
        'artifacts/docs/api/JYPPX.ROCm.MIGraphXSharp.MIGraphXBuildInfo.html'
    )) {
        $path = Join-Path $root $relativePath
        if (-not (Test-Path -LiteralPath $path)) { throw "DocFX output is missing $relativePath." }
    }
}
finally {
    Pop-Location
}
