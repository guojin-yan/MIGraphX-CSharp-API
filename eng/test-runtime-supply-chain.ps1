[CmdletBinding()]
param([string] $Manifest = 'nuget/runtime-manifests/linux-x64.json')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
Import-Module (Join-Path $PSScriptRoot 'runtime-manifest.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'runtime-archive.psm1') -Force
$manifestPath = Join-Path $root $Manifest

function New-Copy { return Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json -AsHashtable }
function Assert-Rejected([string] $Name, [scriptblock] $Mutation) {
    $candidate = New-Copy
    & $Mutation $candidate
    $testRoot = Join-Path $root 'artifacts/runtime-supply-chain-tests'
    New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
    $candidatePath = Join-Path $testRoot (($Name -replace '[^A-Za-z0-9.-]', '-') + '.json')
    [IO.File]::WriteAllText(
        $candidatePath,
        (($candidate | ConvertTo-Json -Depth 40) + "`n"),
        [Text.UTF8Encoding]::new($false))
    try {
        & (Join-Path $PSScriptRoot 'validate-runtime-manifest.ps1') -Manifest $candidatePath
        throw "Negative runtime manifest test unexpectedly passed: $Name"
    } catch {
        if ($_.Exception.Message -like 'Negative runtime manifest test unexpectedly passed:*') { throw }
        Write-Host "Rejected as expected: $Name"
    }
}

$baseline = New-Copy
Assert-MIGraphXRuntimeManifest $baseline
& (Join-Path $PSScriptRoot 'generate-runtime-metadata.ps1') -Manifest $manifestPath -Check
try {
    Assert-MIGraphXRuntimeManifest $baseline -RequireCandidate
    throw 'Deferred runtime unexpectedly passed the candidate gate.'
} catch {
    if ($_.Exception.Message -eq 'Deferred runtime unexpectedly passed the candidate gate.') { throw }
    Write-Host 'Rejected as expected: deferred candidate pack gate'
}

Assert-Rejected 'source hash' { param($m) $m.packages[0].sha256 = '0' * 64 }
Assert-Rejected 'license hash' { param($m) $m.licenses[0].sha256 = '0' * 64 }
Assert-Rejected 'closure hash' { param($m) $m.closure.sha256 = '0' * 64 }
Assert-Rejected 'architecture' { param($m) $m.packages[0].architecture = 'arm64' }
Assert-Rejected 'path traversal' { param($m) $m.files[0].path = 'runtimes/linux-x64/native/../../escape.so' }
Assert-Rejected 'alias identity' { param($m) $m.files[1].sha256 = '0' * 64 }
Assert-Rejected 'SBOM hash' { param($m) $m.metadata.sbom.sha256 = '0' * 64 }
Assert-Rejected 'promotion receipt' { param($m) $m.metadata.promotionReceiptSha256 = '0' * 64 }
Assert-Rejected 'package marker state' { param($m) $m.metadata.packageMarker.status = 'generated' }
Assert-Rejected 'state escalation' { param($m) $m.candidateStaged = $true }
Assert-Rejected 'release authorization' { param($m) $m.releaseAuthorized = $true }
Assert-Rejected 'size' { param($m) $m.size.manifestCanonicalBytes++ }
Assert-Rejected 'topology dependency' { param($m) $m.topology.runtimeDependency.version = '[7.3.0]' }
Assert-Rejected 'duplicate package path' { param($m) $m.files[1].path = $m.files[0].path }
Assert-Rejected 'publication authorization' { param($m) $m.publishAuthorized = $true }
Assert-Rejected 'unresolved dependency' { param($m) $m.files[0].needed += 'libnotallowlisted.so.1' }
Assert-Rejected 'missing payload license' { param($m) $m.licenses = @() }

foreach ($archiveMutation in @(
    @{ Name = 'archive path traversal'; Path = '../escape.so'; Type = '-'; Target = $null },
    @{ Name = 'archive absolute path'; Path = '/etc/escape'; Type = '-'; Target = $null },
    @{ Name = 'archive symlink escape'; Path = 'opt/rocm/lib/escape.so'; Type = 'l'; Target = '../../../../outside' },
    @{ Name = 'archive special device'; Path = 'opt/rocm/dev/kfd'; Type = 'c'; Target = $null }
)) {
    try {
        Assert-MIGraphXArchiveEntry -Path $archiveMutation.Path -Type ([char]$archiveMutation.Type) -LinkTarget $archiveMutation.Target
        throw "Negative runtime archive test unexpectedly passed: $($archiveMutation.Name)"
    } catch {
        if ($_.Exception.Message -like 'Negative runtime archive test unexpectedly passed:*') { throw }
        Write-Host "Rejected as expected: $($archiveMutation.Name)"
    }
}

try {
    & (Join-Path $PSScriptRoot 'pack-runtime.ps1')
    throw 'Deferred controlled runtime pack unexpectedly passed.'
} catch {
    if ($_.Exception.Message -eq 'Deferred controlled runtime pack unexpectedly passed.' -or $_.Exception.Message -notmatch 'MIGRAPHX1001') { throw }
    Write-Host 'Rejected as expected: controlled runtime pack entry point'
}

$directPackOutput = @(& dotnet pack (Join-Path $root 'pack/JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64.csproj') -p:RuntimeControlledPack=true 2>&1)
if ($LASTEXITCODE -eq 0 -or ($directPackOutput -join "`n") -notmatch 'MIGRAPHX1001') {
    throw "Direct runtime pack guard did not fail closed with MIGRAPHX1001:`n$($directPackOutput -join "`n")"
}
Write-Host 'Rejected as expected: direct dotnet pack property bypass'
Write-Host 'Runtime supply-chain positive and 21 fail-closed mutation tests passed.'
