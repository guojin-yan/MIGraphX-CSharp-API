[CmdletBinding()]
param(
    [string] $Manifest = 'nuget/runtime-manifests/linux-x64.json',
    [string] $CacheDirectory = '.cache/runtime/rocm-7.2.1-noble'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$manifestPath = Join-Path $root $Manifest
$testRoot = Join-Path $root 'artifacts/runtime-source-tests'

function Assert-Rejected([string] $Name, [scriptblock] $Mutation) {
    $value = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json -AsHashtable
    & $Mutation $value
    $path = Join-Path $testRoot (($Name -replace '[^A-Za-z0-9.-]', '-') + '.json')
    [IO.File]::WriteAllText($path, (($value | ConvertTo-Json -Depth 40) + "`n"), [Text.UTF8Encoding]::new($false))
    try {
        & (Join-Path $PSScriptRoot 'prepare-runtime.ps1') -Manifest $path -CacheDirectory $CacheDirectory -Offline -VerifyOnly
        throw "Negative runtime source test unexpectedly passed: $Name"
    } catch {
        if ($_.Exception.Message -like 'Negative runtime source test unexpectedly passed:*') { throw }
        Write-Host "Rejected as expected: $Name"
    }
}

New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
& (Join-Path $PSScriptRoot 'prepare-runtime.ps1') -Manifest $manifestPath -CacheDirectory $CacheDirectory -Offline -VerifyOnly
Assert-Rejected 'package-hash' { param($m) $m.packages[0].sha256 = '0' * 64 }
Assert-Rejected 'package-version' { param($m) $m.packages[0].version = '2.15.0-invalid' }
Assert-Rejected 'wrong-architecture' { param($m) $m.packages[0].architecture = 'arm64' }
Assert-Rejected 'unapproved-host' { param($m) $m.packages[0].url = 'https://example.invalid/package.deb' }
Assert-Rejected 'inrelease-hash' { param($m) $m.source.inReleaseSha256 = '1' * 64 }
Write-Host 'Runtime signed-source positive and mutation tests passed.'
