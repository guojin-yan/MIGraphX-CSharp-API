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
if ($RepositoryCommit -notmatch '^[a-f0-9]{40}$') { throw 'Tests require a lowercase 40-character Git SHA.' }
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
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $RepositoryCommit -HipSharpRepositoryRoot $HipSharpRepositoryRoot
    }
    else {
        & (Join-Path $PSScriptRoot 'verify-public-api.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $RepositoryCommit -SkipToolBuild
    }
    & (Join-Path $PSScriptRoot 'build-fake-native.ps1') -Configuration $Configuration
    & (Join-Path $PSScriptRoot 'generate-m2-model.ps1') | Out-Host
    Invoke-DotNet -Arguments @('test', '.\MIGraphXSharp.sln', '-c', $Configuration, '--no-build', '--logger', 'console;verbosity=normal')
    Invoke-DotNet -Arguments @('run', '--project', './smoke/EnvironmentSmokeRunner/EnvironmentSmokeRunner.csproj', '-c', $Configuration, '--no-build')
    $fakeName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c.dll' } else { 'libmigraphx_c.so' }
    $fakePath = Join-Path $root "artifacts\fake-native\$Configuration\$fakeName"
    Invoke-DotNet -Arguments @('run', '--project', './smoke/EnvironmentSmokeRunner/EnvironmentSmokeRunner.csproj', '-c', $Configuration, '--no-build', '--', '--fake-native', $fakePath)
    $m1OnlyName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c_m1_only.dll' } else { 'libmigraphx_c_m1_only.so' }
    $m1OnlyPath = Join-Path $root "artifacts\fake-native\$Configuration\$m1OnlyName"
    $modelPath = Join-Path $root 'artifacts\models\m2-identity-float32.onnx'
    Invoke-DotNet -Arguments @('run', '--project', './smoke/OnnxWorkflowSmokeRunner/OnnxWorkflowSmokeRunner.csproj', '-c', $Configuration, '--no-build', '--', '--expect-frontend-missing', $m1OnlyPath)
    Invoke-DotNet -Arguments @('run', '--project', './smoke/OnnxWorkflowSmokeRunner/OnnxWorkflowSmokeRunner.csproj', '-c', $Configuration, '--no-build', '--', '--fake-native', $fakePath, $modelPath)
    & (Join-Path $PSScriptRoot 'test-interop-paths.ps1') -Configuration $Configuration -NoBuild
}
finally {
    Pop-Location
}
