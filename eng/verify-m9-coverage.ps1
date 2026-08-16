[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$map = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m9-high-level-api-map.json') | ConvertFrom-Json
if ($map.counts.inventory -ne 192 -or $map.counts.supported -ne 80 -or $map.counts.planned -ne 111 -or $map.counts.unsupported -ne 1) {
    throw 'M9 mapping counts must be 80/111/1 over the fixed 192-item inventory.'
}
$expected = @(
    'function:migraphx_onnx_options_set_default_loop_iterations',
    'function:migraphx_onnx_options_set_limit_loop_iterations',
    'function:migraphx_onnx_options_set_external_data_path',
    'function:migraphx_compile_options_set_fast_math',
    'function:migraphx_compile_options_set_exhaustive_tune_flag'
)
$mappings = @($map.mappings)
if ($mappings.Count -ne $expected.Count -or @($mappings | Group-Object id | Where-Object Count -ne 1).Count -ne 0) {
    throw 'M9 must contain five unique option mappings.'
}
foreach ($id in $expected) {
    $item = @($mappings | Where-Object id -eq $id)
    if ($item.Count -ne 1 -or $item[0].supportStatus -ne 'supported' -or @($item[0].publicMembers).Count -eq 0 -or @($item[0].tests).Count -eq 0) {
        throw "M9 mapping is incomplete: $id"
    }
}
$baseline = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\managed-public-api.txt')
foreach ($member in @('SetDefaultLoopIterations', 'SetLimitLoopIterations', 'SetExternalDataPath', 'FastMath', 'ExhaustiveTune')) {
    if (-not $baseline.Contains($member, [StringComparison]::Ordinal)) { throw "M9 public baseline is missing '$member'." }
}
$smoke = Get-Content -Raw -LiteralPath (Join-Path $root 'smoke\OnnxWorkflowSmokeRunner\Program.cs')
$cloud = Get-Content -Raw -LiteralPath (Join-Path $root 'tools\radeon\cloud-test.sh')
if (-not $smoke.Contains('--runtime-options-candidate', [StringComparison]::Ordinal) -or -not $cloud.Contains('--runtime-options-candidate', [StringComparison]::Ordinal)) {
    throw 'M9 cloud option runner is not wired into the credential-free cloud script.'
}
$abiGate = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\verify-m2-abi.ps1')
if (-not $abiGate.Contains('compatibility\m9-high-level-api-map.json', [StringComparison]::Ordinal) -or
    -not $abiGate.Contains('$allowedM9FakeExports', [StringComparison]::Ordinal)) {
    throw 'M9 fake-native exports are not wired into the strict ABI export review.'
}
Write-Output 'M9 coverage gate passed: five inference-option entry points, 80/111/1 aggregate mapping, local contracts, and deferred cloud runner are closed.'
