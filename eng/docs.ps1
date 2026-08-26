[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.0.0',
    [string] $RepositoryCommit,
    [string] $HipSharpRepositoryRoot,
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$RepositoryCommit = if ([string]::IsNullOrWhiteSpace($RepositoryCommit)) { (& git -C $root rev-parse HEAD).Trim() } else { $RepositoryCommit }
if ($RepositoryCommit -notmatch '^[a-f0-9]{40}$') { throw 'Documentation requires a lowercase 40-character Git SHA.' }
Push-Location $root
$resolvedHipSharpRepositoryRoot = $HipSharpRepositoryRoot
if ([string]::IsNullOrWhiteSpace($resolvedHipSharpRepositoryRoot) -and -not [string]::IsNullOrWhiteSpace($env:HIPSHARP_REPOSITORY_ROOT)) {
    $resolvedHipSharpRepositoryRoot = $env:HIPSHARP_REPOSITORY_ROOT
}
$previousHipSharpEnvironment = [Environment]::GetEnvironmentVariable('HipSharpRepositoryRoot', 'Process')
if (-not [string]::IsNullOrWhiteSpace($resolvedHipSharpRepositoryRoot)) {
    [Environment]::SetEnvironmentVariable('HipSharpRepositoryRoot', $resolvedHipSharpRepositoryRoot, 'Process')
}
try {
    & (Join-Path $PSScriptRoot 'verify-m3-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m4-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m5-coverage.ps1') | Out-Host
    $m6Arguments = @{}
    if (-not [string]::IsNullOrWhiteSpace($resolvedHipSharpRepositoryRoot)) { $m6Arguments.HipSharpRepositoryRoot = $resolvedHipSharpRepositoryRoot }
    & (Join-Path $PSScriptRoot 'verify-m6-coverage.ps1') @m6Arguments | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m9-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m10-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m11-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m12-coverage.ps1') | Out-Host
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $RepositoryCommit -HipSharpRepositoryRoot $resolvedHipSharpRepositoryRoot
    }
    else {
        & (Join-Path $PSScriptRoot 'verify-public-api.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $RepositoryCommit -SkipToolBuild
    }
    Invoke-DotNet -Arguments @('tool', 'restore')
    $docfxVersion = ((Get-Content -LiteralPath (Join-Path $root '.config\dotnet-tools.json') -Raw | ConvertFrom-Json).tools.docfx.version)
    $nugetPackages = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) { Join-Path $HOME '.nuget\packages' } else { $env:NUGET_PACKAGES }
    $docfxDll = Join-Path $nugetPackages (Join-Path 'docfx' (Join-Path $docfxVersion 'tools/net10.0/any/docfx.dll'))
    if (Test-Path -LiteralPath $docfxDll) {
        Invoke-DotNet -Arguments @($docfxDll, (Join-Path $root 'docfx.json'), '--warningsAsErrors')
    }
    else {
        Invoke-DotNet -Arguments @('tool', 'run', 'docfx', (Join-Path $root 'docfx.json'), '--warningsAsErrors')
    }
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
    [Environment]::SetEnvironmentVariable('HipSharpRepositoryRoot', $previousHipSharpEnvironment, 'Process')
    Pop-Location
}
