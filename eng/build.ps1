[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.0.0',
    [string] $RepositoryCommit,
    [string] $HipSharpRepositoryRoot,
    [switch] $NoRestore
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$RepositoryCommit = if ([string]::IsNullOrWhiteSpace($RepositoryCommit)) { (& git -C $root rev-parse HEAD).Trim() } else { $RepositoryCommit }
if ($RepositoryCommit -notmatch '^[a-f0-9]{40}$') { throw 'Build requires a lowercase 40-character Git SHA.' }
$properties = @(
    "-p:MIGraphXSharpVersion=$Version",
    "-p:AdapterPackageVersion=$Version",
    "-p:RepositoryCommit=$RepositoryCommit"
)
if (-not [string]::IsNullOrWhiteSpace($HipSharpRepositoryRoot)) {
    $HipSharpRepositoryRoot = (Resolve-Path -LiteralPath $HipSharpRepositoryRoot).Path
    if (-not $HipSharpRepositoryRoot.EndsWith([IO.Path]::DirectorySeparatorChar.ToString(), [StringComparison]::Ordinal)) {
        $HipSharpRepositoryRoot += [IO.Path]::DirectorySeparatorChar
    }
    $properties += "-p:HipSharpRepositoryRoot=$HipSharpRepositoryRoot"
}
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'verify-m3-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m4-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m5-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m6-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m9-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m10-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m11-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m12-coverage.ps1') | Out-Host
    if (-not $NoRestore) {
        Invoke-DotNet -Arguments (@('restore', '.\MIGraphXSharp.sln') + $properties)
    }
    Invoke-DotNet -Arguments (@('build', '.\MIGraphXSharp.sln', '-c', $Configuration, '--no-restore') + $properties)
    # The solution graph can reset Configuration for the external multi-target HipSharp reference.
    # Rebuild the direct consumer project so the test binary uses the same configuration as the candidate.
    Invoke-DotNet -Arguments (@('build', '.\tests\JYPPX.ROCm.MIGraphXSharp.UnitTests\JYPPX.ROCm.MIGraphXSharp.UnitTests.csproj', '-c', $Configuration, '--no-restore') + $properties)
    & (Join-Path $PSScriptRoot 'verify-public-api.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $RepositoryCommit -SkipToolBuild
}
finally {
    Pop-Location
}
