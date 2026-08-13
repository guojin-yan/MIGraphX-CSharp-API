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
    Invoke-DotNet -Arguments @('test', '.\MIGraphXSharp.sln', '-c', $Configuration, '--no-build', '--logger', 'console;verbosity=normal')
    Invoke-DotNet -Arguments @('run', '--project', '.\smoke\EnvironmentSmokeRunner\EnvironmentSmokeRunner.csproj', '-c', $Configuration, '--no-build')
}
finally {
    Pop-Location
}
