[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.0.0',
    [switch] $Runtime,
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$packageDirectory = Join-Path $root 'artifacts\packages'

if ($Runtime) {
    $policy = Get-Content -Raw -LiteralPath (Join-Path $root 'pack\runtime-validation-disclosure-policy.json') | ConvertFrom-Json
    if (-not $policy.runtimePackagingEnabled -or $policy.failClosed) {
        throw "Runtime packaging is disabled and fail-closed: $($policy.reason)"
    }
    throw 'Runtime packaging has no implementation in M0.'
}

Push-Location $root
try {
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
    }
    New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
    $repositoryCommit = (& git rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $repositoryCommit -notmatch '^[a-f0-9]{40}$') {
        throw 'Core packaging requires a committed 40-character Git SHA.'
    }
    Invoke-DotNet -Arguments @(
        'pack',
        '.\src\JYPPX.ROCm.MIGraphX.CSharp.API\JYPPX.ROCm.MIGraphX.CSharp.API.csproj',
        '-c', $Configuration,
        '--no-build',
        ("-p:MIGraphXSharpVersion=$Version"),
        ("-p:RepositoryCommit=$repositoryCommit"),
        '-o', $packageDirectory
    )
    $package = Join-Path $packageDirectory "JYPPX.ROCm.MIGraphX.CSharp.API.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $package)) {
        throw "Expected package was not produced: $package"
    }
    Write-Output $package
}
finally {
    Pop-Location
}
