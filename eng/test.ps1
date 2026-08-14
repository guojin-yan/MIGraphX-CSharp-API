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
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
    }
    & (Join-Path $PSScriptRoot 'build-fake-native.ps1') -Configuration $Configuration
    & (Join-Path $PSScriptRoot 'generate-m2-model.ps1') | Out-Host
    Invoke-DotNet -Arguments @('test', '.\MIGraphXSharp.sln', '-c', $Configuration, '--no-build', '--logger', 'console;verbosity=normal')
    Invoke-DotNet -Arguments @('run', '--project', '.\smoke\EnvironmentSmokeRunner\EnvironmentSmokeRunner.csproj', '-c', $Configuration, '--no-build')
    $fakeName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c.dll' } else { 'libmigraphx_c.so' }
    $fakePath = Join-Path $root "artifacts\fake-native\$Configuration\$fakeName"
    Invoke-DotNet -Arguments @('run', '--project', '.\smoke\EnvironmentSmokeRunner\EnvironmentSmokeRunner.csproj', '-c', $Configuration, '--no-build', '--', '--fake-native', $fakePath)
    $m1OnlyName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c_m1_only.dll' } else { 'libmigraphx_c_m1_only.so' }
    $m1OnlyPath = Join-Path $root "artifacts\fake-native\$Configuration\$m1OnlyName"
    $modelPath = Join-Path $root 'artifacts\models\m2-identity-float32.onnx'
    Invoke-DotNet -Arguments @('run', '--project', '.\smoke\OnnxWorkflowSmokeRunner\OnnxWorkflowSmokeRunner.csproj', '-c', $Configuration, '--no-build', '--', '--expect-frontend-missing', $m1OnlyPath)
    Invoke-DotNet -Arguments @('run', '--project', '.\smoke\OnnxWorkflowSmokeRunner\OnnxWorkflowSmokeRunner.csproj', '-c', $Configuration, '--no-build', '--', '--fake-native', $fakePath, $modelPath)
    & (Join-Path $PSScriptRoot 'test-interop-paths.ps1') -Configuration $Configuration -NoBuild
}
finally {
    Pop-Location
}
