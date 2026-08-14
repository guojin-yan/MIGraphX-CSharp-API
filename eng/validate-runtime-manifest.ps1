[CmdletBinding()]
param(
    [string] $Manifest = 'nuget/runtime-manifests/linux-x64.json',
    [switch] $RequireCandidate,
    [switch] $SkipGeneratedMetadata
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
Import-Module (Join-Path $PSScriptRoot 'runtime-manifest.psm1') -Force
$path = if ([IO.Path]::IsPathRooted($Manifest)) { [IO.Path]::GetFullPath($Manifest) } else { [IO.Path]::GetFullPath((Join-Path $root $Manifest)) }
$schemaPath = Join-Path $root 'nuget/runtime-manifests/schema.json'
$manifestJson = Get-Content -Raw -LiteralPath $path
if (-not ($manifestJson | Test-Json -SchemaFile $schemaPath -ErrorAction Stop)) {
    throw 'Runtime manifest does not satisfy nuget/runtime-manifests/schema.json.'
}
$manifestInfo = Get-MIGraphXRuntimeManifest $path
Assert-MIGraphXRuntimeManifest $manifestInfo.Value -RequireCandidate:$RequireCandidate

if (-not $SkipGeneratedMetadata) {
    $closurePath = Join-Path $root $manifestInfo.Value.closure.path
    if (-not (Test-Path -LiteralPath $closurePath -PathType Leaf) -or (Get-MIGraphXSha256 $closurePath) -ne $manifestInfo.Value.closure.sha256) {
        throw 'Runtime dependency closure is missing or changed.'
    }
    & (Join-Path $PSScriptRoot 'generate-runtime-metadata.ps1') -Manifest $path -Check
}

Write-Host "Runtime manifest validation passed for $($manifestInfo.Value.packageId): $($manifestInfo.Value.technicalStatus)"
