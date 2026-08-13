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
    if (-not $NoRestore) {
        Invoke-DotNet -Arguments @('restore', '.\MIGraphXSharp.sln')
    }
    Invoke-DotNet -Arguments @('build', '.\MIGraphXSharp.sln', '-c', $Configuration, '--no-restore')
}
finally {
    Pop-Location
}
