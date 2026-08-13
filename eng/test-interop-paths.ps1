[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build-fake-native.ps1') -Configuration $Configuration
}

$extension = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c.dll' } else { 'libmigraphx_c.so' }
$nativePath = Join-Path $root "artifacts\fake-native\$Configuration\$extension"
$project = Join-Path $root 'tests\JYPPX.ROCm.MIGraphXSharp.InteropRunner\JYPPX.ROCm.MIGraphXSharp.InteropRunner.csproj'

foreach ($framework in @('net46', 'netcoreapp3.1', 'net7.0', 'net10.0')) {
    if ($framework -eq 'net46' -and -not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
        Write-Output 'net46 fake-native execution is Windows-only; compile coverage remains in the 15-TFM build.'
        continue
    }
    & dotnet run --project $project -c $Configuration -f $framework -- $nativePath
    if ($LASTEXITCODE -ne 0) { throw "Direct P/Invoke representative execution failed for $framework." }
}

Write-Output 'Representative Direct P/Invoke execution passed for net46, netcoreapp3.1, net7.0, and net10.0 against fake-native.'
