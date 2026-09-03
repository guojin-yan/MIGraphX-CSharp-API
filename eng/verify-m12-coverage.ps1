[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot

$matrixPath = Join-Path $root 'compatibility\m12-runtime-cases.json'
$schemaPath = Join-Path $root 'compatibility\schemas\m12-runtime-cases.schema.json'
$promotionPath = Join-Path $root 'compatibility\m12-post-build-runtime-evidence.json'
$promotionSchemaPath = Join-Path $root 'compatibility\schemas\m12-post-build-runtime-evidence.schema.json'
if (-not (Test-Path -LiteralPath $matrixPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $schemaPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $promotionPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $promotionSchemaPath -PathType Leaf)) {
    throw 'M12 runtime matrix, promotion record, or schema is missing.'
}
$matrixText = Get-Content -Raw -LiteralPath $matrixPath
if (-not ($matrixText | Test-Json -SchemaFile $schemaPath)) { throw 'M12 runtime cases do not match their JSON schema.' }
$matrix = $matrixText | ConvertFrom-Json
$promotionText = Get-Content -Raw -LiteralPath $promotionPath
if (-not ($promotionText | Test-Json -SchemaFile $promotionSchemaPath)) { throw 'M12 promotion record does not match its JSON schema.' }
$promotion = $promotionText | ConvertFrom-Json

if ($matrix.stage -ne 'M12' -or $matrix.candidateVersion -ne '0.0.0' -or $matrix.validationStatus -ne 'partially-runtime-executed') {
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
    'm12-operation-materialized-attributes',
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
$promotedCaseIds = @('m12-context-lifetime', 'm12-operation-materialized-attributes')
$retainedCaseIds = @($requiredCaseIds | Where-Object { $_ -notin $promotedCaseIds })
foreach ($case in $cases) {
    $expectedEvidence = if ($case.id -in $promotedCaseIds) { 'runtime-executed' } else { 'runtime-deferred' }
    if ($case.officialEvidence -ne $expectedEvidence) { throw "M12 case evidence does not match the independent promotion decision: $($case.id)" }
    if ([string]::IsNullOrWhiteSpace($case.evidenceBoundary)) { throw "M12 case evidence boundary is missing: $($case.id)" }
    foreach ($fixtureId in @($case.fixtureIds)) {
        if ($fixtureId -notin $fixtureIds) { throw "M12 case references an unknown fixture: $($case.id) -> $fixtureId" }
    }
}
if ($matrix.review.candidateResultLabel -ne 'runtime-candidate-executed-review-required' -or
    $matrix.review.promotionRecord -ne 'm12-post-build-runtime-evidence.json' -or
    $matrix.review.historicalMapsRemainUnchanged -ne $true) {
    throw 'M12 review and promotion boundary is incomplete.'
}

if ($promotion.stage -ne 'M12' -or
    $promotion.sourceSha -ne 'b53689ba3831ce721875d3e5bb4d370ae8a737e6' -or
    $promotion.candidateVersion -ne '0.0.0' -or
    $promotion.externalRecord -ne 'Radeon_Cloud/records/20260827-0242-b53689b-m12-runtime' -or
    $promotion.reviewState -ne 'passed' -or
    $promotion.reviewedEvidence -ne 'runtime-executed' -or
    $promotion.candidateResultLabel -ne 'runtime-candidate-executed-review-required' -or
    $promotion.candidateReviewState -ne 'candidate-record-verified' -or
    $promotion.candidatePromotionState -ne 'not-requested' -or
    $promotion.candidateExecutedCaseCount -ne 11 -or
    $promotion.candidateDeferredCaseCount -ne 8 -or
    $promotion.resultSha256 -ne '3493a5c5b7023df19d074b634f7696ec93a71d65e46bebc201bcbdb695ca6b09' -or
    $promotion.reviewSha256 -ne '4cc8178deb831091ed7c645cb0ce0ccb43a3905a09e9b3972e98e8b536452250' -or
    $promotion.summarySha256 -ne 'f226766aa9c1c321f3e5495b044007065927869cd2089cdc2821e007cd21eaa0' -or
    $promotion.runMetadataSha256 -ne '47e091ee9c616aeff478ea926936c4fbaa3a8132aa0e60a423480312176ea35c' -or
    $promotion.environment.os -ne 'Ubuntu 24.04' -or
    $promotion.environment.nativeProvider -ne 'MIGraphX 2.15.0 with ROCm 7.2.1' -or
    $promotion.environment.headerSha256 -ne 'a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2' -or
    $promotion.environment.librarySha256 -ne '3b012a738306e2d4499d0aa0dce7b73f96a96209ade45369ad9194c208801aff' -or
    $promotion.historicalMapsRemainUnchanged -ne $true) {
    throw 'M12 promotion identity, evidence hashes, or environment drifted.'
}
$promotionIds = @($promotion.promotions | ForEach-Object id)
$retainedIds = @($promotion.retained | ForEach-Object id)
if (@($promotion.promotions).Count -ne $promotedCaseIds.Count -or
    @($promotion.promotions | Where-Object status -ne 'runtime-executed').Count -ne 0 -or
    (Compare-Object $promotedCaseIds $promotionIds)) {
    throw 'M12 promoted case set drifted.'
}
if (@($promotion.retained).Count -ne $retainedCaseIds.Count -or
    @($promotion.retained | Where-Object status -ne 'runtime-deferred').Count -ne 0 -or
    (Compare-Object $retainedCaseIds $retainedIds)) {
    throw 'M12 retained case set drifted.'
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
if ($coreTypes -ne 45 -or $coreMembers -ne 309) { throw "M12 core API baseline drifted: $coreTypes/$coreMembers, expected 45/309." }

$m10 = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m10-high-level-api-map.json') | ConvertFrom-Json
if ($m10.counts.supported -ne 84 -or $m10.counts.planned -ne 107 -or $m10.counts.unsupported -ne 1) {
    throw 'M12 must not mutate the historical 84/107/1 compatibility map.'
}

$sourceChecks = @{
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXShape.cs' = @('CreateScalar', 'CreateWithStrides', 'GetDimensionLength')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXArgument.cs' = @('CreateEmpty', 'Generate', 'Save', 'Clone', 'NativeBorrowedOutput.RequireHandle')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXProgram.cs' = @('ParseTfFile', 'QuantizeInt8', 'GetExperimentalContext', 'GetMainModule')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXOnnxWorkflow.cs' = @('GetSingleParameterName', 'Marshal.WriteIntPtr(names, IntPtr.Zero)', 'migraphx_program_parameter_shapes_names')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXGraph.cs' = @('MIGraphXModule', 'MIGraphXInstruction', 'MIGraphXOperation', 'Create(string nativeLibraryPath, string name)', 'DecodeRequiredBuffer', 'byte.MaxValue', 'Clone()', 'argument.Shape.ByteCount != 0', 'success with null buffer')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\Interop\StrictUtf8String.cs' = @('DecodeRequiredBuffer', 'length < capacity', 'success with unwritten or unterminated UTF-8 buffer', 'success with empty UTF-8 buffer', 'success with invalid UTF-8 buffer')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\Interop\NativeValueOutput.cs' = @('ProgramParameterShapesSizeRaw', 'ShapeLengthsRaw', 'ReadSizeT', 'ReadInt32', 'ReadPointerAndSize', 'success without writing {outputType} output', 'Sentinel = 0xA5')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\Interop\NativeBorrowedOutput.cs' = @('RequireHandle', 'success with null borrowed handle')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXOperationAttributes.cs' = @('ForReshape', 'ForTranspose', 'ForSlice', 'ForMultibroadcast', 'ForTopK', 'SetInt32', 'SetUInt32', 'SetInt64', 'SetUInt64', 'SetSingle', 'SetDouble', 'SetBoolean', 'SetString', 'SetNull', 'SetInt32Array', 'SetUInt32Array', 'SetInt64Array', 'SetUInt64Array', 'SetSingleArray', 'SetDoubleArray', 'SetBooleanArray', 'SetStringArray', 'RequireValues')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXTfOptions.cs' = @('SetInputParameterShape', 'SetOutputNames')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXQuantization.cs' = @('MIGraphXQuantizeInt8Options', 'MIGraphXQuantizeFp8Options')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXContext.cs' = @('GetQueue', 'Finish')
    'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXExperimentalCustomOp.cs' = @('SetCompute', 'InvokeCompute', 'NormalizeCallbackStatus', 'undefined status', 'WriteCallbackException', '(bytes[copy] & 0xC0) == 0x80', 'Register', 'CopyState', 'clone.IsAllocated', 'GC.KeepAlive(callback)')
    'native\fake-migraphx\fake_m12.inc' = @('m12_live_count', 'migraphx_experimental_custom_op_register', 'fake_invoke_custom_callbacks', 'fake_invoke_custom_state_copy_callbacks', 'fake_invoke_custom_compute_with_error_buffer', 'dispatch_provider_callback', 'provider_callback_dispatch', 'callback_message')
    'native\fake-migraphx\fake_migraphx.c' = @('fake_custom_compute', 'fake_custom_compute_shape', 'fake_custom_output_alias', 'fake_custom_runs_on_offload_target', 'fake_custom_state_copy_count', 'fake_custom_state_delete_count', 'fake_provider_callback_message', 'fake_set_skip_string', 'skip_string_for("migraphx_program_parameter_shapes_names")', 'fake_set_skip_output', 'skip_output_for', 'take_named_null_for("migraphx_argument_buffer")', 'take_named_null_for("migraphx_arguments_get")', 'take_named_null_for("migraphx_argument_shape")')
    'eng\generate-m12-fixtures.ps1' = @('m12-tensorflow-minimal.pb', 'm12-calibration-map.json', 'tensorflow-graphdef')
    'compatibility\schemas\m12-calibration-map.schema.json' = @('migraphx-calibration-map', 'float32', 'zeroPoint')
    'compatibility\m12-post-build-runtime-evidence.json' = @('b53689ba3831ce721875d3e5bb4d370ae8a737e6', 'candidate-record-verified', 'm12-context-lifetime', 'm12-operation-materialized-attributes', 'runtime-executed', 'runtime-deferred')
    'compatibility\schemas\m12-post-build-runtime-evidence.schema.json' = @('post-build-external-runtime-promotion', 'candidateExecutedCaseCount', 'candidateDeferredCaseCount')
    'tests\JYPPX.ROCm.MIGraphXSharp.UnitTests\M12LocalInterfaceTests.cs' = @('ShapeAndArgumentFactories', 'GraphEditingAndContextViews', 'TensorFlowAndQuantization', 'CustomOpClone', 'CustomOpStateCopyDeletePreservesManagedIdentity', 'CustomOpReplacingAndClearingComputeCallbackKeepsReplayCurrent', 'CustomOpCallbackRootLastsThroughNativeOwnerLifetime', 'DisposedCustomOpReleasesCallbackRootsWhileWrapperRemainsAlive', 'CallbackLifetimeCapture', 'CustomOpCallbackSetterFailurePreservesPreviousCallbackAndReplay', 'FakeProviderDispatchInvokesRegisteredShapeCallbackThroughGraphPath', 'FakeProviderDispatchContainsShapeCallbackExceptionThroughGraphPath', 'FakeProviderDispatchIgnoresUnrelatedOperationName', 'CustomOpRegisterFailureLeavesRegistryUnchangedAndRetryWorks', 'CustomOpCallbackExceptionsBecomeNativeStatusAndUtf8Message', 'CustomOpCallbacksNormalizeUndefinedStatusValues', 'managed_utf8_exception_test', 'new UTF8Encoding(false, true)', 'InvokeCustomCallbacks', 'callbackInvocations', 'ProviderCallbackMessage', 'CustomOpCallbackSettersRaceDisposeRemainFailClosed', 'DeferredNegativeBoundariesAndConcurrentDispose', 'OperationAttributeSurfaceRemainsClosedOverArbitraryVariadicAbi', 'ModuleSurfaceRemainsProgramBoundWithoutIndependentOwner', 'QuantizeInt8OptionSnapshotFailsAfterDispose', 'TensorFlowOutputNameSnapshotFailsAfterDispose', 'SetSkipString', 'success with unwritten or unterminated UTF-8 buffer', 'SetNullOutput("migraphx_argument_buffer")', 'success with null buffer')
    'tests\JYPPX.ROCm.MIGraphXSharp.UnitTests\M4ManagedObjectTests.cs' = @('SnapshotsHandleMultipleItemsAndRejectMalformedNativeCollections', 'SetSkipOutput', 'SetNullOutput', 'success with null borrowed handle', 'migraphx_arguments_get', 'migraphx_argument_shape', 'migraphx_shape_type', 'migraphx_shape_lengths', 'migraphx_shape_strides', 'migraphx_shape_elements', 'migraphx_shape_bytes', 'migraphx_arguments_size', 'migraphx_program_parameter_shapes_size', 'migraphx_shapes_size')
    'tests\JYPPX.ROCm.MIGraphXSharp.UnitTests\M1NativeVerticalTests.cs' = @('SetSkipString("migraphx_program_parameter_shapes_names")', 'success with null UTF-8 pointer', 'AssertNoNativeLeaks(controls)')
    'tests\JYPPX.ROCm.MIGraphXSharp.UnitTests\M5DynamicShapeCacheTests.cs' = @('NativeBooleanResultsRejectInvalidOrUnwrittenValues', 'SetSkipOutput', 'migraphx_dynamic_dimensions_size', 'success without writing size_t output')
    'tests\JYPPX.ROCm.MIGraphXSharp.UnitTests\M10CapabilityEqualityTests.cs' = @('OnnxRegistryCopiesStableUtf8AndFailsClosedForEveryInjectedFault', 'SetSkipOutput', 'migraphx_get_onnx_operators_size', 'success without writing size_t output')
    'tests\JYPPX.ROCm.MIGraphXSharp.InteropRunner\Program.cs' = @('m12-cross-target', 'm12Passed')
    'tools\m12-runtime-probe\M12RuntimeProbe.csproj' = @('PackageReference', 'M12PackageVersion')
    'tools\m12-runtime-probe\Program.cs' = @('runtime-candidate-executed-review-required', 'm12-shape-argument-factories', 'scalar-empty', 'source-dispose', 'collection-clone', 'assignToCloneSha256', 'module-collections', 'Edited graph output', 'RunTensorFlowParse', 'RunQuantizationOptions', 'RunCustomOpRegistration', 'RunConcurrentDispose', 'RunNegativeVariadicOperationBoundary', 'RunNegativeModuleOwnerBoundary', 'RunNegativeBorrowedDeviceCloneBoundary', 'borrowedExternalLeaseSha256', 'no-gpu-allocation', 'cases.Take(9)', 'IncludeDeferred', '--include-deferred cannot be combined with --case', 'DeferredCases', 'case-stages.jsonl', 'TensorFlowFixture', 'CalibrationMap', 'tensorflowFixtureSha256', 'calibrationFixtureSha256', 'negativeBoundariesSha256')
    'tools\m12-cross-target-probe\M12CrossTargetProbe.csproj' = @('netcoreapp3.1;net7.0;net10.0', 'PackageReference', 'M12PackageVersion')
    'tools\m12-cross-target-probe\Program.cs' = @('runtime-candidate-executed-review-required', 'm12-cross-target-abi', 'InteropCompilationProbe', 'DllImport', 'LibraryImport', 'Identity output differs', 'M12 materialized operation creation or clone differs')
    'tools\m12-runtime-probe\run.sh' = @('timeout --kill-after=10s 300s', 'timeout --kill-after=10s 180s', 'M12PackageVersion', 'm12-cross-target-probe', '--no-cache --force-evaluate', 'packageSourceMapping', 'JYPPX.ROCm.MIGraphX.*', 'Microsoft.*', 'NETStandard.Library', 'environmentChanged', '--tensorflow-fixture', '--calibration-map', '--include-deferred', 'all-candidate', 'includeDeferred', 'm12-negative-variadic-operation', 'm12-negative-module-owner', 'm12-negative-borrowed-device-clone', 'm12-cross-target-$framework.json', 'crossTargetFrameworks', 'evidence record must be isolated from repository and package feed', 'evidence record directory must be new or empty before a new run')
    'tools\m12-runtime-probe\review.ps1' = @('candidate-record-verified', 'm12-cross-target-abi', 'crossTargetFrameworkCount', 'DllImport', 'LibraryImport', 'promotionState', 'TensorFlowFixturePath', 'CalibrationMapPath', 'tensorflowFixtureSha256', 'calibrationFixtureSha256', 'negative-boundaries.txt', 'borrowedExternalLeaseSha256', 'Artifact manifest path must be absolute', 'Artifact manifest path escapes evidence record', 'Duplicate artifact manifest path', 'Artifact manifest is missing required review input')
    'tools\m12-provider-callback-probe\Program.cs' = @('fake-native-provider-dispatch', '--provider-fixture', 'm12_runtime_provider_callback_probe', 'callback-invoked-controlled-rejection', 'callback-not-observed', 'expectedInformational')
    'tools\m12-provider-callback-probe\provider-callback-probe.sh' = @('provider-callback-invocation', 'fake-native-provider-dispatch', 'promotionRequested=false')
    'tools\m12-provider-callback-probe\review.ps1' = @('provider-callback-record-verified', 'providerFixture', 'promotionState', 'Artifact manifest path escapes evidence record')
    'tools\radeon\cloud-test.sh' = @('m12-runtime-probe/run.sh', '--include-deferred', 'm12-candidate', 'm12-review.txt', 'm12CaseFilter', 'm12ExecutedCases', '"m12ExecutedCases":15', '"m12FunctionalCases":14', '"m12CrossTargetFrameworks":3', 'candidate-record-verified')
}
foreach ($relativePath in $sourceChecks.Keys) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "M12 source is missing: $relativePath" }
    $source = Get-Content -Raw -LiteralPath $path
    foreach ($token in $sourceChecks[$relativePath]) {
        if (-not $source.Contains($token, [StringComparison]::Ordinal)) { throw "M12 source check is missing '$token' in $relativePath" }
    }
}

$operationAttributesSource = Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\MIGraphXOperationAttributes.cs')
foreach ($forbiddenToken in @('params object', 'object[]', 'IntPtr', 'UnmanagedCallersOnly')) {
    if ($operationAttributesSource.Contains($forbiddenToken, [StringComparison]::Ordinal)) {
        throw "Operation attribute source must not expose arbitrary ABI token '$forbiddenToken'."
    }
}
if (-not $operationAttributesSource.Contains('SetBooleanArray', [StringComparison]::Ordinal)) {
    throw 'Operation attribute source lost the reviewed Boolean-array typed value.'
}

$packageConsumerFrameworks = @('net46', 'netcoreapp3.1', 'net7.0', 'net10.0')
$packageConsumerTokens = @('MIGraphXOperationAttributes.ForReshape', 'MIGraphXOperationAttributes.ForTranspose', 'MIGraphXOperationAttributes.ForSlice', 'MIGraphXOperationAttributes.ForMultibroadcast', 'MIGraphXOperationAttributes.ForTopK', 'SetInt32', 'SetUInt32', 'SetInt64', 'SetUInt64', 'SetSingle', 'SetDouble', 'SetBoolean', 'SetString', 'SetNull', 'SetInt32Array', 'SetUInt32Array', 'SetInt64Array', 'SetUInt64Array', 'SetSingleArray', 'SetDoubleArray', 'SetBooleanArray', 'SetStringArray')
foreach ($framework in $packageConsumerFrameworks) {
    $consumerPath = Join-Path $root (Join-Path 'tests\fixtures\package-consumers' (Join-Path $framework 'Program.cs'))
    if (-not (Test-Path -LiteralPath $consumerPath -PathType Leaf)) { throw "M12 package consumer is missing: $framework" }
    $consumerSource = Get-Content -Raw -LiteralPath $consumerPath
    foreach ($token in $packageConsumerTokens) {
        if (-not $consumerSource.Contains($token, [StringComparison]::Ordinal)) {
            throw "M12 package consumer is missing '$token': $framework"
        }
    }
}

$probeProject = Get-Content -Raw -LiteralPath (Join-Path $root 'tools\m12-runtime-probe\M12RuntimeProbe.csproj')
if ($probeProject.Contains('ProjectReference', [StringComparison]::Ordinal)) {
    throw 'M12 runtime probe must remain package-only and cannot add a ProjectReference.'
}
$crossTargetProbeProject = Get-Content -Raw -LiteralPath (Join-Path $root 'tools\m12-cross-target-probe\M12CrossTargetProbe.csproj')
if ($crossTargetProbeProject.Contains('ProjectReference', [StringComparison]::Ordinal)) {
    throw 'M12 cross-target probe must remain package-only and cannot add a ProjectReference.'
}

$design = Get-Content -Raw -LiteralPath (Join-Path $root 'docs\design\m12-local-interface-expansion.md')
if (-not $design.Contains('Local validation record', [StringComparison]::Ordinal) -or
    -not $design.Contains('real MIGraphX runtime', [StringComparison]::Ordinal) -or
    -not $design.Contains('migraphx_operation_create', [StringComparison]::Ordinal) -or
    -not $design.Contains('migraphx_module_create', [StringComparison]::Ordinal) -or
    -not $design.Contains('reflection contract', [StringComparison]::Ordinal)) {
    throw 'M12 design record is missing local validation or deferred-boundary statements.'
}

Write-Output "M12 coverage gate passed: $($cases.Count) runtime cases, $($fixtures.Count) fixtures, 2 reviewed promotions, 13 retained cases, 45/309 API baseline, and local source/test closure."
