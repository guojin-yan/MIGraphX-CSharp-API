[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Release')

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$model = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility/m3-normalized-api.json') | ConvertFrom-Json
$subset = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility/m2-binding-subset.json') | ConvertFrom-Json
$headerNames = @($model.functions.cName | Sort-Object -Unique)
$expected = @($subset.functions.cName)
foreach ($mapName in @('m4-high-level-api-map.json', 'm9-high-level-api-map.json', 'm10-high-level-api-map.json')) {
    $map = Get-Content -Raw -LiteralPath (Join-Path $root "compatibility/$mapName") | ConvertFrom-Json
    $expected += @($map.mappings | Where-Object { $_.kind -eq 'function' -and $_.supportStatus -eq 'supported' } | ForEach-Object cName)
}
$expected = @($expected | Sort-Object -Unique)
if (@($expected | Where-Object { $_ -notin $headerNames }).Count -gt 0) { throw 'Test export contract contains functions outside the frozen header.' }

$fakeName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c.dll' } else { 'libmigraphx_c.so' }
$fakePath = Join-Path (Get-ArtifactsRoot) "fake-native/$Configuration/$fakeName"
if (-not (Test-Path -LiteralPath $fakePath -PathType Leaf)) { throw 'Build the substitute library with eng/build-fake-native.ps1 first.' }
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    if (-not $installation) { throw 'Visual Studio C++ tools were not found.' }
    $toolsVersion = Get-ChildItem -LiteralPath (Join-Path $installation 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $dumpbin = Join-Path $toolsVersion.FullName 'bin/Hostx64/x64/dumpbin.exe'
    $exportOutput = @(& $dumpbin /nologo /exports $fakePath)
} else {
    $exportOutput = @(& (Get-Command nm -ErrorAction Stop).Source -D --defined-only $fakePath)
}
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect substitute-library exports.' }
$exports = @($exportOutput | ForEach-Object {
    if ($_ -match '\s(?<name>migraphx_[a-z0-9_]+)\s*$') { $Matches.name }
} | Sort-Object -Unique)
$missing = @($expected | Where-Object { $_ -notin $exports })
$unexpected = @($exports | Where-Object { $_ -notin $headerNames })
if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Substitute-library export mismatch. Missing: $($missing -join ', '); outside frozen header: $($unexpected -join ', ')."
}
Write-Output "Substitute-library export contract passed: $($expected.Count) required functions; all $($exports.Count) MIGraphX exports belong to the frozen header."
