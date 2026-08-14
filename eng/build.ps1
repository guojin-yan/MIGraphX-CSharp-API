[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoRestore
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'verify-m3-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m4-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m5-coverage.ps1') | Out-Host
    & (Join-Path $PSScriptRoot 'verify-m6-coverage.ps1') | Out-Host
    if (-not $NoRestore) {
        Invoke-DotNet -Arguments @('restore', '.\MIGraphXSharp.sln')
    }
    Invoke-DotNet -Arguments @('build', '.\MIGraphXSharp.sln', '-c', $Configuration, '--no-restore')
}
finally {
    Pop-Location
}
