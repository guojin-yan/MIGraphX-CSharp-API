[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })][string] $RecordDirectory,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CorePackagePath,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $SourceSha,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string] $CoreSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$record = (Resolve-Path -LiteralPath $RecordDirectory).Path
$raw = Join-Path $record 'raw'
$resultPath = Join-Path $raw 'm12-functional.json'
$metadataPath = Join-Path $raw 'run-metadata.json'
$manifestPath = Join-Path $raw 'artifact-hashes.txt'
$stagePath = Join-Path $raw 'case-stages.jsonl'
foreach ($path in @($resultPath, $metadataPath, $manifestPath, $stagePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required review input is missing: $path" }
}

function Get-Sha256([string] $Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
$metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
if ($result.evidence -ne 'runtime-candidate-executed-review-required' -or $result.state -ne 'executed' -or
    $result.sourceSha -ne $SourceSha -or $result.packageVersion -ne '0.0.0') {
    throw 'M12 candidate result identity or execution state is invalid.'
}
if ($metadata.evidence -ne 'runtime-candidate-executed-review-required' -or $metadata.sourceSha -ne $SourceSha -or
    $metadata.version -ne '0.0.0' -or $metadata.functionalExitCode -ne 0 -or
    $metadata.functionalSessionTimeoutSeconds -ne 300 -or $metadata.sessionKillAfterSeconds -ne 10 -or
    $metadata.caseFilter -ne 'all' -or
    $metadata.environmentChanged -ne $false -or $metadata.promotionRequested -ne $false) {
    throw 'M12 runner metadata is invalid or requests an unauthorized promotion.'
}

$expectedCases = @(
    'm12-shape-argument-factories',
    'm12-argument-persistence-clone',
    'm12-assign-to-clone',
    'm12-graph-parent-lease',
    'm12-graph-editing',
    'm12-context-lifetime'
)
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
if (Compare-Object $expectedCases @($result.cases.id) -or @($result.cases | Where-Object state -ne 'passed').Count -ne 0) {
    throw 'M12 executed case set is incomplete or failed.'
}
if (Compare-Object $expectedDeferred @($result.deferredCaseIds)) { throw 'M12 deferred case set drifted.' }
$stages = @(Get-Content -LiteralPath $stagePath | ForEach-Object { $_ | ConvertFrom-Json })
foreach ($caseId in $expectedCases) {
    foreach ($state in @('started', 'completed')) {
        if (@($stages | Where-Object { $_.caseId -eq $caseId -and $_.stage -eq 'case' -and $_.state -eq $state }).Count -ne 1) {
            throw "M12 case stage trace is incomplete: $caseId/$state"
        }
    }
}
if ((Get-Sha256 $CorePackagePath) -ne $CoreSha256) { throw 'Core package hash mismatch.' }

$manifestFailures = [Collections.Generic.List[string]]::new()
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ($line -notmatch '^([a-f0-9]{64})\s+(.+)$') { throw "Malformed artifact hash line: $line" }
    if (-not (Test-Path -LiteralPath $Matches[2] -PathType Leaf) -or (Get-Sha256 $Matches[2]) -ne $Matches[1]) { $manifestFailures.Add($Matches[2]) }
}
if ($manifestFailures.Count -ne 0) { throw "Artifact hash mismatches: $($manifestFailures -join ', ')" }

$review = [ordered]@{
    schemaVersion = '1.0.0'
    reviewState = 'candidate-record-verified'
    evidence = 'runtime-candidate-executed-review-required'
    promotionState = 'not-requested'
    sourceSha = $SourceSha
    packageVersion = '0.0.0'
    corePackageSha256 = $CoreSha256
    executedCaseCount = @($result.cases).Count
    deferredCaseCount = @($result.deferredCaseIds).Count
    candidateResultSha256 = Get-Sha256 $resultPath
    artifactHashesRecomputed = $true
    verifiedUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$reviewPath = Join-Path $record 'review.json'
[IO.File]::WriteAllText($reviewPath, ($review | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $reviewPath
