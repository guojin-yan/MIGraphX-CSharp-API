[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
& (Join-Path $PSScriptRoot 'generate-m4-map.ps1') -Verify | Out-Host

$map = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m4-high-level-api-map.json') | ConvertFrom-Json
$ownership = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m4-public-ownership.json') | ConvertFrom-Json
$baseline = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\managed-public-api.txt')

if ($map.mappings.Count -ne 192 -or $map.counts.supported -ne 74 -or $map.counts.planned -ne 117 -or $map.counts.unsupported -ne 1) {
    throw 'M5 high-level coverage counts drifted from 74/117/1 over 192 inventory items.'
}
if (@($map.mappings | Group-Object id | Where-Object Count -ne 1).Count -ne 0) {
    throw 'M4 high-level coverage contains duplicate ids.'
}
if (@($map.mappings | Where-Object { $_.supportStatus -eq 'supported' -and ($_.publicMembers.Count -eq 0 -or $_.tests.Count -eq 0 -or -not $_.ownership) }).Count -ne 0) {
    throw 'Every supported M4 mapping must name public members, ownership, and behavior tests.'
}
if (@($map.mappings | Where-Object { $_.supportStatus -ne 'supported' -and $_.publicMembers.Count -ne 0 }).Count -ne 0) {
    throw 'Planned and unsupported M4 mappings must not claim public members.'
}
if (@($ownership.types).Count -ne 13 -or @($ownership.types | Group-Object type | Where-Object Count -ne 1).Count -ne 0) {
    throw 'M5 ownership manifest must contain thirteen unique public ownership records.'
}

foreach ($required in @(
    'MIGraphXShapeDataType', 'MIGraphXShape', 'MIGraphXDynamicDimension', 'MIGraphXTarget', 'MIGraphXProgram',
    'MIGraphXArgument', 'MIGraphXOnnxOptions', 'MIGraphXCompileOptions',
    'MIGraphXParameterMap', 'MIGraphXArgumentCollection', 'MIGraphXFileOptions', 'MIGraphXModelCache', 'MIGraphXCacheMetadata',
    'ParseOnnxFile', 'ParseOnnxBuffer', 'GetParameterShapes', 'GetOutputShapes', 'ToArray<T>'
)) {
    if (-not $baseline.Contains($required)) { throw "M4 public baseline is missing '$required'." }
}

$operation = $map.mappings | Where-Object id -eq 'function:migraphx_operation_create'
if ($operation.supportStatus -ne 'unsupported') { throw 'migraphx_operation_create must remain unsupported in M4.' }

Write-Output 'M5 coverage gate passed: 74 supported low-level inventory items, 13 ownership records.'
