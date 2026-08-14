[CmdletBinding()]
param(
    [ValidateSet('linux-x64')][string] $Rid = 'linux-x64',
    [string] $Manifest = 'nuget/runtime-manifests/linux-x64.json',
    [string] $OutputDirectory = 'artifacts/runtime-packages'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$policy = Get-Content -Raw -LiteralPath (Join-Path $root 'pack/runtime-validation-disclosure-policy.json') | ConvertFrom-Json
if (-not $policy.runtimePackagingEnabled -or $policy.failClosed -or -not $policy.candidateStaged -or -not $policy.verified) {
    throw "MIGRAPHX1001: Runtime packaging is fail-closed at $($policy.technicalStatus): $($policy.reason)"
}
& (Join-Path $PSScriptRoot 'validate-runtime-manifest.ps1') -Manifest $Manifest -RequireCandidate
throw 'MIGRAPHX1001: No runtime pack is permitted without a clean-SHA candidate attestation and exact external promotion receipt.'
