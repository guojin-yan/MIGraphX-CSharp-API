[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot

$matrixPath = Join-Path $root 'compatibility\m12-runtime-cases.json'
$schemaPath = Join-Path $root 'compatibility\schemas\m12-runtime-cases.schema.json'
if (-not (Test-Path -LiteralPath $matrixPath -PathType Leaf) -or -not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) {
    throw 'M12 runtime matrix or schema is missing.'
}
$matrixText = Get-Content -Raw -LiteralPath $matrixPath
if (-not ($matrixText | Test-Json -SchemaFile $schemaPath)) { throw 'M12 runtime cases do not match their JSON schema.' }
$matrix = $matrixText | ConvertFrom-Json

if ($matrix.stage -ne 'M12' -or $matrix.candidateVersion -ne '0.0.0' -or $matrix.validationStatus -ne 'runtime-deferred') {
    throw 'M12 matrix identity or validation status drifted.'
}
if ($matrix.authorization.realRuntimeAuthorized -ne $false -or
    $matrix.authorization.packageProbeAuthorized -ne $false -or
    $matrix.authorization.environmentChangesAuthorized -ne $false) {
    throw 'M12 real-runtime and environment authorization must remain false until explicitly granted.'
}
if ($matrix.environment.requiredOs -ne 'Linux' -or
    $matrix.environment.requiredNativeProvider -ne 'MIGraphX 2.15.0 with ROCm 7.2.1' -or
    $matrix.environment.localOs -ne 'Windows' -or
    $matrix.environment.localNativeProviderAvailable -ne $false -or
    $matrix.environment.localFakeNativeAvailable -ne $true) {
    throw 'M12 environment boundary drifted.'
}
$expectedTfms = @('net46', 'net461', 'net462', 'net47', 'net471', 'net472', 'net48', 'net481', 'netcoreapp3.1', 'net5.0', 'net6.0', 'net7.0', 'net8.0', 'net9.0', 'net10.0')
if (@($matrix.environment.managedTargetFrameworks).Count -ne $expectedTfms.Count -or
    (@($matrix.environment.managedTargetFrameworks) -join ';') -ne ($expectedTfms -join ';')) {
    throw 'M12 managed target framework matrix drifted.'
}

$fixtures = @($matrix.fixtures)
if ($fixtures.Count -lt 4 -or @($fixtures | Group-Object id | Where-Object Count -ne 1).Count -ne 0) {
    throw 'M12 must contain at least four unique fixture records.'
}
$fixtureIds = @($fixtures | ForEach-Object id)
$cases = @($matrix.cases)
if ($cases.Count -lt 14 -or @($cases | Group-Object id | Where-Object Count -ne 1).Count -ne 0) {
    throw 'M12 must contain at least fourteen unique runtime cases.'
}
$requiredCaseIds = @(
    'm12-shape-argument-factories',
    'm12-argument-persistence-clone',
    'm12-assign-to-clone',
    'm12-graph-parent-lease',
    'm12-graph-editing',
    'm12-tensorflow-parse',
    'm12-quantization-options',
    'm12-context-lifetime',
    'm12-custom-op-registration',
    'm12-negative-borrowed-device-clone',
    'm12-negative-variadic-operation',
    'm12-negative-module-owner',
    'm12-concurrent-dispose',
    'm12-cross-target-abi'
)
foreach ($id in $requiredCaseIds) {
    if (@($cases | Where-Object id -eq $id).Count -ne 1) { throw "M12 required case is missing or duplicated: $id" }
}
foreach ($case in $cases) {
    if ($case.officialEvidence -ne 'runtime-deferred') { throw "M12 case is promoted before independent review: $($case.id)" }
    foreach ($fixtureId in @($case.fixtureIds)) {
        if ($fixtureId -notin $fixtureIds) { throw "M12 case references an unknown fixture: $($case.id) -> $fixtureId" }
    }
}
if ($matrix.review.candidateResultLabel -ne 'runtime-candidate-executed-review-required' -or
    $matrix.review.historicalMapsRemainUnchanged -ne $true) {
    throw 'M12 review and promotion boundary is incomplete.'
}

$m11Path = Join-Path $root 'compatibility\m11-runtime-cases.json'
$m11 = Get-Content -Raw -LiteralPath $m11Path | ConvertFrom-Json
$m11Fixtures = @($m11.fixtures)
foreach ($pair in @(@('m12-identity-float32-1x4', 'identity-float32-1x4'), @('m12-dynamic-identity-float32', 'dynamic-identity-float32-batchx4'))) {
    $m12Fixture = $fixtures | Where-Object id -eq $pair[0]
    $m11Fixture = $m11Fixtures | Where-Object id -eq $pair[1]
    if ($null -eq $m12Fixture -or $null -eq $m11Fixture -or $m12Fixture.sha256 -ne $m11Fixture.sha256 -or $m12Fixture.fileName -ne $m11Fixture.fileName) {
        throw "M12 fixture identity does not match the frozen M11 fixture: $($pair[0])"
    }
}

$fixtureOutput = Join-Path $root 'artifacts\models\m12-coverage'
$generatedFixtures = @(& (Join-Path $PSScriptRoot 'generate-m12-fixtures.ps1') -OutputDirectory $fixtureOutput)
if ($generatedFixtures.Count -ne 2) { throw 'M12 fixture generator must produce exactly two new fixtures.' }
foreach ($fixture in @($fixtures | Where-Object { $_.id -in @('m12-tensorflow-minimal', 'm12-quantization-calibration') })) {
    $actual = @($generatedFixtures | Where-Object FileName -eq $fixture.fileName)
    if ($actual.Count -ne 1 -or $actual[0].Sha256 -ne $fixture.sha256 -or $actual[0].License -ne $fixture.license) {
        throw "M12 fixture identity drifted: $($fixture.id)"
    }
}
$calibrationPath = Join-Path $fixtureOutput 'm12-calibration-map.json'
$calibrationText = Get-Content -Raw -LiteralPath $calibrationPath
if (-not ($calibrationText | Test-Json -SchemaFile (Join-Path $root 'compatibility\schemas\m12-calibration-map.schema.json'))) {
    throw 'M12 calibration map fixture does not match its schema.'
}
$tensorflow = @($generatedFixtures | Where-Object FileName -eq 'm12-tensorflow-minimal.pb')
if ($tensorflow.Count -ne 1 -or $tensorflow[0].Format -ne 'tensorflow-graphdef' -or $tensorflow[0].NodeCount -ne 2) {
    throw 'M12 TensorFlow fixture metadata is incomplete.'
}

$baseline = Get-Content -LiteralPath (Join-Path $root 'compatibility\managed-public-api.txt')
$coreTypes = @($baseline | Where-Object { $_.StartsWith('T|', [StringComparison]::Ordinal) }).Count
$coreMembers = @($baseline | Where-Object { -not $_.StartsWith('#', [StringComparison]::Ordinal) -and -not $_.StartsWith('T|', [StringComparison]::Ordinal) -and $_.Length -ne 0 }).Count
if ($coreTypes -ne 45 -or $coreMembers -ne 303) { throw "M12 core API baseline drifted: $coreTypes/$coreMembers, expected 45/303." }

$m10 = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m10-high-level-api-map.json') | ConvertFrom-Json
if ($m10.counts.supported -ne 84 -or $m10.counts.planned -ne 107 -or $m10.counts.unsupported -ne 1) {
    throw 'M12 must not mutate the historical 84/107/1 compatibility map.'
}

$sourceChecks = @{
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXShape.cs' = @('CreateScalar', 'CreateWithStrides', 'GetDimensionLength')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXArgument.cs' = @('CreateEmpty', 'Generate', 'Save', 'Clone')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXProgram.cs' = @('ParseTfFile', 'QuantizeInt8', 'GetExperimentalContext', 'GetMainModule')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXGraph.cs' = @('MIGraphXModule', 'MIGraphXInstruction', 'MIGraphXOperation', 'Create(string nativeLibraryPath, string name)', 'Clone()')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXTfOptions.cs' = @('SetInputParameterShape', 'SetOutputNames')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXQuantization.cs' = @('MIGraphXQuantizeInt8Options', 'MIGraphXQuantizeFp8Options')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXContext.cs' = @('GetQueue', 'Finish')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXExperimentalCustomOp.cs' = @('SetCompute', 'Register', 'CopyState')
    'native\fake-migraphx\fake_m12.inc' = @('m12_live_count', 'migraphx_experimental_custom_op_register')
    'eng\generate-m12-fixtures.ps1' = @('m12-tensorflow-minimal.pb', 'm12-calibration-map.json', 'tensorflow-graphdef')
    'compatibility\schemas\m12-calibration-map.schema.json' = @('migraphx-calibration-map', 'float32', 'zeroPoint')
    'tests\JYPPX.ROCm.MIGraphXSharp.UnitTests\M12LocalInterfaceTests.cs' = @('ShapeAndArgumentFactories', 'GraphEditingAndContextViews', 'TensorFlowAndQuantization', 'CustomOpClone', 'DeferredNegativeBoundariesAndConcurrentDispose')
    'tests\JYPPX.ROCm.MIGraphXSharp.InteropRunner\Program.cs' = @('m12-cross-target', 'm12Passed')
    'tools\m12-runtime-probe\M12RuntimeProbe.csproj' = @('PackageReference', 'M12PackageVersion')
    'tools\m12-runtime-probe\Program.cs' = @('runtime-candidate-executed-review-required', 'm12-shape-argument-factories', 'DeferredCases', 'case-stages.jsonl', 'TensorFlowFixture', 'CalibrationMap', 'tensorflowFixtureSha256', 'calibrationFixtureSha256')
    'tools\m12-runtime-probe\run.sh' = @('timeout --kill-after=10s 300s', 'M12PackageVersion', '--no-cache --force-evaluate', 'environmentChanged', '--tensorflow-fixture', '--calibration-map')
    'tools\m12-runtime-probe\review.ps1' = @('candidate-record-verified', 'm12-cross-target-abi', 'promotionState', 'TensorFlowFixturePath', 'CalibrationMapPath', 'tensorflowFixtureSha256', 'calibrationFixtureSha256')
}
foreach ($relativePath in $sourceChecks.Keys) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "M12 source is missing: $relativePath" }
    $source = Get-Content -Raw -LiteralPath $path
    foreach ($token in $sourceChecks[$relativePath]) {
        if (-not $source.Contains($token, [StringComparison]::Ordinal)) { throw "M12 source check is missing '$token' in $relativePath" }
    }
}

$probeProject = Get-Content -Raw -LiteralPath (Join-Path $root 'tools\m12-runtime-probe\M12RuntimeProbe.csproj')
if ($probeProject.Contains('ProjectReference', [StringComparison]::Ordinal)) {
    throw 'M12 runtime probe must remain package-only and cannot add a ProjectReference.'
}

$design = Get-Content -Raw -LiteralPath (Join-Path $root 'docs\design\m12-local-interface-expansion.md')
if (-not $design.Contains('Local validation record', [StringComparison]::Ordinal) -or
    -not $design.Contains('real MIGraphX runtime', [StringComparison]::Ordinal) -or
    -not $design.Contains('migraphx_operation_create', [StringComparison]::Ordinal) -or
    -not $design.Contains('migraphx_module_create', [StringComparison]::Ordinal)) {
    throw 'M12 design record is missing local validation or deferred-boundary statements.'
}

Write-Output "M12 coverage gate passed: $($cases.Count) runtime cases, $($fixtures.Count) fixtures, 45/303 API baseline, deferred promotion, and local source/test closure."
