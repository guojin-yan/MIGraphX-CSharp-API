[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$headerHash = 'a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2'
$implementationCommit = 'de19b73ad280476e646512b847885eda100ec35e'
$ids = @(
    'function:migraphx_get_onnx_operators_size',
    'function:migraphx_get_onnx_operator_name_at_index',
    'function:migraphx_shape_equal',
    'function:migraphx_argument_equal',
    'function:migraphx_program_equal'
)

$mapPath = Join-Path $root 'compatibility\m10-high-level-api-map.json'
$mapSchemaPath = Join-Path $root 'compatibility\schemas\m10-high-level-api-map.schema.json'
$ownershipPath = Join-Path $root 'compatibility\m10-public-ownership.json'
$ownershipSchemaPath = Join-Path $root 'compatibility\schemas\m10-public-ownership.schema.json'
$mapText = Get-Content -Raw -LiteralPath $mapPath
if (-not ($mapText | Test-Json -SchemaFile $mapSchemaPath)) { throw 'M10 map does not match its JSON schema.' }
$map = $mapText | ConvertFrom-Json
if ($map.sourceHeaderSha256 -ne $headerHash -or -not $map.sourceImplementation.Contains($implementationCommit, [StringComparison]::Ordinal)) {
    throw 'M10 source header or fixed upstream implementation identity drifted.'
}
if ($map.counts.inventory -ne 192 -or $map.counts.supported -ne 84 -or $map.counts.planned -ne 107 -or $map.counts.unsupported -ne 1) {
    throw 'M10 mapping counts must be 84/107/1 over the fixed 192-item inventory.'
}
$mappings = @($map.mappings)
if ($mappings.Count -ne 5 -or @($mappings | Group-Object id | Where-Object Count -ne 1).Count -ne 0 -or @($mappings | Where-Object id -notin $ids).Count -ne 0) {
    throw 'M10 must contain exactly the five unique reviewed candidate mappings.'
}
foreach ($item in $mappings) {
    if ($item.id -ne "function:$($item.cName)" -or @($item.upstreamEvidence).Count -eq 0 -or [string]::IsNullOrWhiteSpace($item.decisionRationale)) {
        throw "M10 mapping identity or decision evidence is incomplete: $($item.id)"
    }
    if ($item.decision -eq 'adopted') {
        if ($item.supportStatus -ne 'supported' -or $item.validationLevel -ne 'fake-native-executed' -or
            @($item.publicMembers).Count -eq 0 -or @($item.tests).Count -eq 0 -or $null -ne $item.notAdoptedReason) {
            throw "Adopted M10 mapping is incomplete: $($item.id)"
        }
    }
    elseif ($item.decision -eq 'retained-planned') {
        if ($item.supportStatus -ne 'planned' -or $item.validationLevel -ne 'statically-verified' -or
            @($item.publicMembers).Count -ne 0 -or @($item.tests).Count -ne 0 -or [string]::IsNullOrWhiteSpace($item.notAdoptedReason)) {
            throw "Retained-planned M10 mapping is incomplete: $($item.id)"
        }
    }
    else { throw "Unknown M10 decision: $($item.decision)" }
}

$base = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m4-high-level-api-map.json') | ConvertFrom-Json
$m5 = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m5-high-level-api-map.json') | ConvertFrom-Json
$m6 = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m6-high-level-api-map.json') | ConvertFrom-Json
$m9 = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m9-high-level-api-map.json') | ConvertFrom-Json
if ($base.counts.supported -ne 74 -or $base.counts.planned -ne 117 -or $base.counts.unsupported -ne 1 -or
    $m5.counts.inventory -ne 192 -or @($m5.supportedM5Capabilities).Count -ne 22 -or
    $m6.counts.supported -ne 75 -or $m9.counts.supported -ne 80) {
    throw 'The M5/M6/M9 aggregate sources no longer match the reviewed M10 closure inputs.'
}
$aggregate = @{}
foreach ($item in $base.mappings) { $aggregate[$item.id] = $item.supportStatus }
foreach ($overlay in @($m6, $m9, $map)) {
    foreach ($item in $overlay.mappings) {
        if (-not $aggregate.ContainsKey($item.id)) { throw "Overlay id is absent from the fixed inventory: $($item.id)" }
        $aggregate[$item.id] = $item.supportStatus
    }
}
$computedSupported = @($aggregate.Values | Where-Object { $_ -eq 'supported' }).Count
$computedPlanned = @($aggregate.Values | Where-Object { $_ -eq 'planned' }).Count
$computedUnsupported = @($aggregate.Values | Where-Object { $_ -eq 'unsupported' }).Count
if ($aggregate.Count -ne 192 -or $computedSupported -ne 84 -or $computedPlanned -ne 107 -or $computedUnsupported -ne 1) {
    throw "Computed M10 closure is $computedSupported/$computedPlanned/$computedUnsupported over $($aggregate.Count), expected 84/107/1 over 192."
}
if ($aggregate['function:migraphx_shape_equal'] -ne 'planned' -or $aggregate['function:migraphx_operation_create'] -ne 'unsupported') {
    throw 'Shape equality must remain planned and the sole variadic operation must remain unsupported.'
}

$normalized = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m3-normalized-api.json') | ConvertFrom-Json
$inventory = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m3-api-inventory.json') | ConvertFrom-Json
$summary = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m3-coverage-summary.json') | ConvertFrom-Json
if ($normalized.source.headerSha256 -ne $headerHash -or $normalized.source.peeledCommit -ne $implementationCommit -or
    @($normalized.functions).Count -ne 159 -or @($inventory.items).Count -ne 192 -or
    $summary.managedEntryPointCount -ne 158 -or $summary.unsupportedFunctionCount -ne 1) {
    throw 'The fixed M3 normalized model, inventory, or managed EntryPoint count drifted.'
}
foreach ($id in $ids) {
    if (@($normalized.functions | Where-Object id -eq $id).Count -ne 1 -or @($inventory.items | Where-Object id -eq $id).Count -ne 1) {
        throw "M10 candidate is missing from the normalized model or inventory: $id"
    }
}
$generated = (Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Generated\NativeMethods.DllImport.g.cs')) +
    (Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Generated\NativeMethods.LibraryImport.g.cs'))
foreach ($id in $ids) {
    $entryPoint = $id.Substring('function:'.Length)
    if (@([regex]::Matches($generated, "EntryPoint\s*=\s*`"$entryPoint`"")).Count -ne 2) {
        throw "M10 candidate must have one DllImport and one LibraryImport declaration: $entryPoint"
    }
}

$ownershipText = Get-Content -Raw -LiteralPath $ownershipPath
if (-not ($ownershipText | Test-Json -SchemaFile $ownershipSchemaPath)) { throw 'M10 ownership evidence does not match its JSON schema.' }
$ownership = $ownershipText | ConvertFrom-Json
if ($ownership.stage -ne 'M10' -or $ownership.validationLevel -ne 'fake-native-executed' -or
    @($ownership.records).Count -ne 4 -or @($ownership.records | Group-Object subject | Where-Object Count -ne 1).Count -ne 0) {
    throw 'M10 ownership evidence must contain four unique records.'
}
$baseline = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\managed-public-api.txt')
foreach ($member in @('MIGraphXOnnxWorkflow.GetRegisteredOperators', 'MIGraphXArgument.HasSameNativeContent', 'MIGraphXProgram.HasSameNativeContent')) {
    if (-not $baseline.Contains($member, [StringComparison]::Ordinal)) { throw "M10 public baseline is missing '$member'." }
}
if ($baseline.Contains('MIGraphXShape.HasSameNativeContent', [StringComparison]::Ordinal)) {
    throw 'MIGraphXShape must remain an owner-free managed snapshot without native equality projection.'
}
$publicSources = @{
    'MIGraphXOnnxWorkflow.GetRegisteredOperators' = Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXOnnxWorkflow.cs'
    'MIGraphXArgument.HasSameNativeContent' = Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXArgument.cs'
    'MIGraphXProgram.HasSameNativeContent' = Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXProgram.cs'
}
foreach ($member in $publicSources.Keys) {
    $methodName = $member.Substring($member.LastIndexOf('.') + 1)
    $source = Get-Content -Raw -LiteralPath $publicSources[$member]
    if (-not $source.Contains($methodName, [StringComparison]::Ordinal) -or
        $source -notmatch '[\u3400-\u9fff]' -or $source -notmatch '[A-Za-z]') {
        throw "M10 public source or bilingual XML is missing for '$member'."
    }
    if (@($ownership.records | Where-Object subject -eq $member).Count -ne 1) {
        throw "M10 ownership evidence is missing for '$member'."
    }
}
$bridge = Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Interop\NativeM10Methods.cs')
foreach ($entryPoint in @('migraphx_get_onnx_operators_size', 'migraphx_get_onnx_operator_name_at_index', 'migraphx_argument_equal', 'migraphx_program_equal')) {
    if (-not $bridge.Contains($entryPoint, [StringComparison]::Ordinal)) { throw "M10 internal bridge is missing: $entryPoint" }
}
$fake = Get-Content -Raw -LiteralPath (Join-Path $root 'native\fake-migraphx\fake_migraphx.c')
foreach ($entryPoint in @('migraphx_get_onnx_operators_size', 'migraphx_get_onnx_operator_name_at_index', 'migraphx_argument_equal', 'migraphx_program_equal')) {
    if (-not $fake.Contains("EXPORT migraphx_status $entryPoint", [StringComparison]::Ordinal)) { throw "fake-native M10 behavior is missing: $entryPoint" }
}
if ($fake.Contains('EXPORT migraphx_status migraphx_shape_equal', [StringComparison]::Ordinal)) {
    throw 'fake-native must not claim execution for retained-planned shape equality.'
}
$tests = Get-Content -Raw -LiteralPath (Join-Path $root 'tests\JYPPX.ROCm.MIGraphXSharp.UnitTests\M10CapabilityEqualityTests.cs')
foreach ($name in @('OnnxRegistryCopiesStableUtf8AndFailsClosedForEveryInjectedFault', 'ArgumentContentComparisonHandlesIndependentValuesFailuresConcurrencyAndDispose', 'ProgramContentComparisonUsesOrderedLocksAndKeepsHandlesAliveAgainstDispose')) {
    if (-not $tests.Contains($name, [StringComparison]::Ordinal)) { throw "M10 behavior test is missing: $name" }
}
$interopRunner = Get-Content -Raw -LiteralPath (Join-Path $root 'tests\JYPPX.ROCm.MIGraphXSharp.InteropRunner\Program.cs')
foreach ($mode in @('--expect-m10-missing', '--expect-m10-equality-missing')) {
    if (-not $interopRunner.Contains($mode, [StringComparison]::Ordinal)) { throw "M10 isolated missing-export test mode is missing: $mode" }
}
$abiGate = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\verify-m2-abi.ps1')
if (-not $abiGate.Contains('compatibility\m10-high-level-api-map.json', [StringComparison]::Ordinal) -or
    -not $abiGate.Contains('$allowedM10FakeExports', [StringComparison]::Ordinal)) {
    throw 'M10 fake-native exports are not wired into the strict ABI export review.'
}
foreach ($path in @(
    'docs\design\m10-onnx-registry-native-comparison.md',
    'docs\validation\m10-local-validation.md',
    'docs\validation\m10-runtime-plan.md',
    'docs\articles\m10-explainable-c-api-introspection.md'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $path) -PathType Leaf)) { throw "M10 documentation is missing: $path" }
}

Write-Output 'M10 coverage gate passed: four adopted entry points, one retained-planned equality decision, 84/107/1 computed closure, and ownership/test evidence are closed.'
