[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $IndexPath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CorePackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $AdapterPackagePath,
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string] $Version = '0.9.0-rc.1',
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $RepositoryCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'release-evidence.psm1') -Force
$indexPath = (Resolve-Path -LiteralPath $IndexPath).Path
$directory = Split-Path -Parent $indexPath
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
if ($index.schemaVersion -ne '1.0.0' -or $index.version -ne $Version -or $index.repositoryCommit -ne $RepositoryCommit -or
    $index.evidenceLevel -ne 'release-candidate-local' -or $index.publicationAuthorized -ne $false) {
    throw 'Release evidence identity or status is incorrect.'
}

$actualPackages = @(
    Get-ReleasePackageIdentity -Path $CorePackagePath
    Get-ReleasePackageIdentity -Path $AdapterPackagePath
)
foreach ($actual in $actualPackages) {
    $record = @($index.packages | Where-Object { $_.id -eq $actual.id })
    if ($record.Count -ne 1 -or $record[0].version -ne $actual.version -or $record[0].sha256 -ne $actual.sha256 -or
        $record[0].normalizedContentSha256 -ne $actual.normalizedContentSha256) {
        throw "Release evidence package mismatch for $($actual.id)."
    }
}
foreach ($evidence in $index.evidence) {
    $path = Join-Path $directory $evidence.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-ReleaseSha256 -Path $path) -ne $evidence.sha256) {
        throw "Release evidence file mismatch: $($evidence.path)"
    }
}

$sbom = Get-Content -Raw -LiteralPath (Join-Path $directory 'm8-managed.sbom.cdx.json') | ConvertFrom-Json
if ($sbom.bomFormat -ne 'CycloneDX' -or $sbom.specVersion -ne '1.5' -or @($sbom.components | Where-Object type -eq 'file').Count -eq 0) {
    throw 'Managed product SBOM is incomplete.'
}
$provenance = Get-Content -Raw -LiteralPath (Join-Path $directory 'm8-managed.provenance.json') | ConvertFrom-Json
if ($provenance.predicateType -ne 'https://slsa.dev/provenance/v1' -or $provenance.predicate.runDetails.metadata.publicationAuthorized -ne $false) {
    throw 'Managed provenance boundary is incorrect.'
}
Write-Output "M8 managed release evidence verified: $indexPath"
