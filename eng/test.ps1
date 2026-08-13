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
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
    }
    & (Join-Path $PSScriptRoot 'build-fake-native.ps1') -Configuration $Configuration
    Invoke-DotNet -Arguments @('test', '.\MIGraphXSharp.sln', '-c', $Configuration, '--no-build', '--logger', 'console;verbosity=normal')
    Invoke-DotNet -Arguments @('run', '--project', '.\smoke\EnvironmentSmokeRunner\EnvironmentSmokeRunner.csproj', '-c', $Configuration, '--no-build')
    $fakeName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c.dll' } else { 'libmigraphx_c.so' }
    $fakePath = Join-Path $root "artifacts\fake-native\$Configuration\$fakeName"
    Invoke-DotNet -Arguments @('run', '--project', '.\smoke\EnvironmentSmokeRunner\EnvironmentSmokeRunner.csproj', '-c', $Configuration, '--no-build', '--', '--fake-native', $fakePath)
    & (Join-Path $PSScriptRoot 'test-interop-paths.ps1') -Configuration $Configuration -NoBuild
}
finally {
    Pop-Location
}
