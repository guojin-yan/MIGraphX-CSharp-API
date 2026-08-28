[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })][string] $RecordDirectory,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CorePackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $TensorFlowFixturePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CalibrationMapPath,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $SourceSha,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string] $CoreSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$record = (Resolve-Path -LiteralPath $RecordDirectory).Path
$raw = Join-Path $record 'raw'
$recordRoot = [IO.Path]::GetFullPath($record).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resultPath = Join-Path $raw 'm12-functional.json'
$metadataPath = Join-Path $raw 'run-metadata.json'
$manifestPath = Join-Path $raw 'artifact-hashes.txt'
$stagePath = Join-Path $raw 'case-stages.jsonl'
$identitiesPath = Join-Path $raw 'identities.txt'
foreach ($path in @($resultPath, $metadataPath, $manifestPath, $stagePath, $identitiesPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required review input is missing: $path" }
}

function Get-Sha256([string] $Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
$metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
$includeDeferred = $false
$includeDeferredProperty = $metadata.PSObject.Properties['includeDeferred']
if ($null -ne $includeDeferredProperty) { $includeDeferred = [bool]$includeDeferredProperty.Value }
$expectedCaseFilter = if ($includeDeferred) { 'all-candidate' } else { 'all' }
if ($result.evidence -ne 'runtime-candidate-executed-review-required' -or $result.state -ne 'executed' -or
    $result.sourceSha -ne $SourceSha -or $result.packageVersion -ne '0.0.0') {
    throw 'M12 candidate result identity or execution state is invalid.'
}
if ($metadata.evidence -ne 'runtime-candidate-executed-review-required' -or $metadata.sourceSha -ne $SourceSha -or
    $metadata.version -ne '0.0.0' -or $metadata.functionalExitCode -ne 0 -or
    $metadata.functionalSessionTimeoutSeconds -ne 300 -or $metadata.sessionKillAfterSeconds -ne 10 -or
    $metadata.caseFilter -ne $expectedCaseFilter -or
    $metadata.environmentChanged -ne $false -or $metadata.promotionRequested -ne $false) {
    throw 'M12 runner metadata is invalid or requests an unauthorized promotion.'
}

$expectedCases = @(
    'm12-shape-argument-factories',
    'm12-argument-persistence-clone',
    'm12-assign-to-clone',
    'm12-graph-parent-lease',
    'm12-graph-editing',
    'm12-operation-materialized-attributes',
    'm12-context-lifetime',
    'm12-negative-variadic-operation',
    'm12-negative-module-owner'
)
if ($includeDeferred) {
    $expectedCases += @(
        'm12-tensorflow-parse',
        'm12-quantization-options',
        'm12-custom-op-registration',
        'm12-concurrent-dispose'
    )
}
$expectedDeferred = @(
    'm12-tensorflow-parse',
    'm12-quantization-options',
    'm12-custom-op-registration',
    'm12-negative-borrowed-device-clone',
    'm12-negative-variadic-operation',
    'm12-negative-module-owner',
    'm12-concurrent-dispose',
    'm12-cross-target-abi'
)
if ((Compare-Object $expectedCases @($result.cases.id)) -or (@($result.cases | Where-Object state -ne 'passed').Count -ne 0)) {
    throw 'M12 executed case set is incomplete or failed.'
}
if (Compare-Object $expectedDeferred @($result.deferredCaseIds)) { throw 'M12 deferred case set drifted.' }
if ($includeDeferred -and ($null -eq $result.artifacts -or
    $result.artifacts.customOpCallbackExecutionVerified -ne 'false')) {
    throw 'Full M12 candidate must keep custom-op callback execution explicitly unverified.'
}
$stages = @(Get-Content -LiteralPath $stagePath | ForEach-Object { $_ | ConvertFrom-Json })
foreach ($caseId in $expectedCases) {
    foreach ($state in @('started', 'completed')) {
        if (@($stages | Where-Object { $_.caseId -eq $caseId -and $_.stage -eq 'case' -and $_.state -eq $state }).Count -ne 1) {
            throw "M12 case stage trace is incomplete: $caseId/$state"
        }
    }
}
$operationAttributesPath = Join-Path $record 'operation-attributes.txt'
if (-not (Test-Path -LiteralPath $operationAttributesPath -PathType Leaf)) { throw 'Operation-attribute observation artifact is missing.' }
$expectedOperationAttributes = @(
    'reshape|{dims: [1, 4]}|reshape',
    'transpose|{permutation: [1, 0]}|transpose',
    'slice|{axes: [0], starts: [0], ends: [1]}|slice',
    'multibroadcast|{out_lens: [1, 4]}|multibroadcast',
    'topk|{axis: 1, k: 1, largest: true}|topk'
)
if (Compare-Object $expectedOperationAttributes @(Get-Content -LiteralPath $operationAttributesPath)) {
    throw 'Operation-attribute observations drifted.'
}
if ($null -eq $result.artifacts -or
    $result.artifacts.operationAttributesSha256 -ne (Get-Sha256 $operationAttributesPath)) {
    throw 'Operation-attribute artifact hash is missing or drifted.'
}
$negativeBoundariesPath = Join-Path $record 'negative-boundaries.txt'
if (-not (Test-Path -LiteralPath $negativeBoundariesPath -PathType Leaf)) { throw 'Negative-boundary observation artifact is missing.' }
$expectedNegativeBoundaries = @(
    'variadic-operation|two-constrained-create-overloads|no-object-pointer-or-params-object',
    'module-owner|no-public-module-constructor-or-static-factory|program-bound-create-module-only'
)
if (Compare-Object $expectedNegativeBoundaries @(Get-Content -LiteralPath $negativeBoundariesPath)) {
    throw 'Negative-boundary observations drifted.'
}
if ($null -eq $result.artifacts -or
    $result.artifacts.negativeBoundariesSha256 -ne (Get-Sha256 $negativeBoundariesPath)) {
    throw 'Negative-boundary artifact hash is missing or drifted.'
}
if ((Get-Sha256 $CorePackagePath) -ne $CoreSha256) { throw 'Core package hash mismatch.' }
$expectedTensorFlowFixtureSha = 'de8be9fda62bbbffb72ce46ac91426b336be60f882e227b6e71e1407c584740e'
$expectedCalibrationFixtureSha = '15f8698707b49e1c92021d833bc0b79c1455f777241e80a7e500619309eda1af'
if ((Get-Sha256 $TensorFlowFixturePath) -ne $expectedTensorFlowFixtureSha) { throw 'TensorFlow fixture hash mismatch.' }
if ((Get-Sha256 $CalibrationMapPath) -ne $expectedCalibrationFixtureSha) { throw 'Calibration map fixture hash mismatch.' }
if ($null -eq $result.fixtureHashes -or
    $result.fixtureHashes.tensorflowFixtureSha256 -ne $expectedTensorFlowFixtureSha -or
    $result.fixtureHashes.calibrationFixtureSha256 -ne $expectedCalibrationFixtureSha) {
    throw 'Candidate result fixture identities are missing or drifted.'
}
$identityLines = Get-Content -LiteralPath $identitiesPath
foreach ($expectedLine in @(
    "tensorflowFixtureSha256=$expectedTensorFlowFixtureSha",
    "calibrationFixtureSha256=$expectedCalibrationFixtureSha"
)) {
    if ($expectedLine -notin $identityLines) { throw "Candidate identity record is missing: $expectedLine" }
}

$manifestFailures = [Collections.Generic.List[string]]::new()
$manifestPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Resolve-ManifestPath([string] $Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw "Artifact manifest path must be absolute: $Path" }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Artifact manifest path is missing: $Path" }
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    if (-not $resolvedPath.StartsWith($recordRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact manifest path escapes evidence record: $Path"
    }
    return [IO.Path]::GetFullPath($resolvedPath)
}
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ($line -notmatch '^([a-f0-9]{64})\s+(.+)$') { throw "Malformed artifact hash line: $line" }
    $path = Resolve-ManifestPath $Matches[2]
    if (-not $manifestPaths.Add($path)) { throw "Duplicate artifact manifest path: $path" }
    if ((Get-Sha256 $path) -ne $Matches[1]) { $manifestFailures.Add($path) }
}
if ($manifestFailures.Count -ne 0) { throw "Artifact hash mismatches: $($manifestFailures -join ', ')" }
foreach ($requiredPath in @($resultPath, $metadataPath, $stagePath, $identitiesPath, $operationAttributesPath, $negativeBoundariesPath)) {
    $resolvedRequiredPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $requiredPath).Path)
    if (-not $manifestPaths.Contains($resolvedRequiredPath)) {
        throw "Artifact manifest is missing required review input: $requiredPath"
    }
}

$review = [ordered]@{
    schemaVersion = '1.0.0'
    reviewState = 'candidate-record-verified'
    evidence = 'runtime-candidate-executed-review-required'
    promotionState = 'not-requested'
    sourceSha = $SourceSha
    packageVersion = '0.0.0'
    caseFilter = $expectedCaseFilter
    includeDeferred = $includeDeferred
    corePackageSha256 = $CoreSha256
    tensorflowFixtureSha256 = $expectedTensorFlowFixtureSha
    calibrationFixtureSha256 = $expectedCalibrationFixtureSha
    executedCaseCount = @($result.cases).Count
    deferredCaseCount = @($result.deferredCaseIds).Count
    candidateResultSha256 = Get-Sha256 $resultPath
    artifactHashesRecomputed = $true
    verifiedUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$reviewPath = Join-Path $record 'review.json'
[IO.File]::WriteAllText($reviewPath, ($review | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $reviewPath
