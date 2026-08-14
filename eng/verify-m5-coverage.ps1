[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
& (Join-Path $PSScriptRoot 'generate-m4-map.ps1') -Verify | Out-Host
$map = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m4-high-level-api-map.json') | ConvertFrom-Json
if ($map.counts.supported -ne 74 -or $map.counts.planned -ne 117 -or $map.counts.unsupported -ne 1) { throw 'M5 mapping counts must be 74/117/1.' }
$m5Map = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m5-high-level-api-map.json') | ConvertFrom-Json
if ($m5Map.counts.inventory -ne 192 -or @($m5Map.supportedM5Capabilities).Count -ne 22) { throw 'M5 capability map is incomplete.' }
$ownership = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m4-public-ownership.json') | ConvertFrom-Json
if (@($ownership.types).Count -ne 13) { throw 'M5 ownership closure must contain 13 records.' }
$baseline = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\managed-public-api.txt')
foreach ($required in @('MIGraphXDynamicDimension', 'MIGraphXFileOptions', 'MIGraphXModelCache', 'MIGraphXCacheMetadata', 'SetDynamicInputParameterShape', 'CreateDynamic', 'static MIGraphXProgram Load', 'void Save(')) {
    if (-not $baseline.Contains($required)) { throw "M5 public baseline is missing '$required'." }
}
Write-Output 'M5 coverage gate passed: dynamic shape, save/load, cache metadata, and ownership mappings are closed.'
