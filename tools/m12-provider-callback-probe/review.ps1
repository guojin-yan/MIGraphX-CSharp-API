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
$recordRoot = [IO.Path]::GetFullPath($record).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resultPath = Join-Path $raw 'provider-callback.json'
$metadataPath = Join-Path $raw 'run-metadata.json'
$identityPath = Join-Path $raw 'identities.txt'
$manifestPath = Join-Path $raw 'artifact-hashes.txt'
foreach ($path in @($resultPath, $metadataPath, $identityPath, $manifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required provider callback input is missing: $path" }
}

function Get-Sha256([string] $Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
function Resolve-ManifestPath([string] $Path) {
    if (-not [IO.Path]::IsPathRooted($Path)) { throw "Artifact manifest path must be absolute: $Path" }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Artifact manifest path is missing: $Path" }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (-not $resolved.StartsWith($recordRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Artifact manifest path escapes evidence record: $Path" }
    return [IO.Path]::GetFullPath($resolved)
}

$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
$metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
if ($result.evidence -ne 'runtime-candidate-executed-review-required' -or
    $result.sourceSha -ne $SourceSha -or $result.expectedVersion -ne '0.0.0' -or
    $result.operationName -ne 'm12_runtime_provider_callback_probe' -or
    $result.state -ne 'callback-invoked-controlled-rejection' -or
    $result.registrationState -ne 'registered' -or
    ($result.graphState -ne 'instruction-created' -and $result.graphState -ne 'compiled' -and $result.graphState -ne 'provider-dispatch-rejected') -or
    $result.controlledFailure -ne $true -or
    $result.promotionState -ne 'not-requested' -or
    $result.callbackInvocations.computeShape -le 0 -or $result.nativeFailureStatus -ne 4) {
    throw 'Provider callback result did not capture the required controlled invocation boundary.'
}
if ($metadata.sourceSha -ne $SourceSha -or $metadata.version -ne '0.0.0' -or
    $metadata.probeExitCode -ne 0 -or $metadata.promotionRequested -ne $false -or
    $metadata.probeKind -ne 'provider-callback-invocation' -or $metadata.controlledRejection -ne $true) {
    throw 'Provider callback probe metadata is invalid or requests promotion.'
}
if ($metadata.providerFixture -ne $result.providerFixture -or
    ($metadata.providerFixture -ne 'none' -and $metadata.providerFixture -ne 'fake-native-provider-dispatch')) {
    throw 'Provider callback fixture identity is invalid.'
}
if ((Get-Sha256 $CorePackagePath) -ne $CoreSha256) { throw 'Core package hash mismatch.' }
if (-not ((Get-Content -LiteralPath $identityPath) -contains "sourceSha=$SourceSha")) { throw 'Source identity is missing.' }
if (-not ((Get-Content -LiteralPath $identityPath) -contains "coreSha256=$CoreSha256")) { throw 'Core package identity is missing.' }

$manifestPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$manifestFailures = [Collections.Generic.List[string]]::new()
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ($line -notmatch '^([a-f0-9]{64})\s+(.+)$') { throw "Malformed artifact hash line: $line" }
    $path = Resolve-ManifestPath $Matches[2]
    if (-not $manifestPaths.Add($path)) { throw "Duplicate artifact manifest path: $path" }
    if ((Get-Sha256 $path) -ne $Matches[1]) { $manifestFailures.Add($path) }
}
if ($manifestFailures.Count -ne 0) { throw "Artifact hash mismatches: $($manifestFailures -join ', ')" }
foreach ($required in @($resultPath, $metadataPath, $identityPath)) {
    $resolved = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $required).Path)
    if (-not $manifestPaths.Contains($resolved)) { throw "Artifact manifest is missing required input: $required" }
}

$review = [ordered]@{
    schemaVersion = '1.0.0'
    reviewState = 'provider-callback-record-verified'
    evidence = 'runtime-candidate-executed-review-required'
    promotionState = 'not-requested'
    sourceSha = $SourceSha
    packageVersion = '0.0.0'
    callbackInvocationObserved = $true
    controlledFailure = $true
    nativeFailureStatus = 4
    artifactHashesRecomputed = $true
    verifiedUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$reviewPath = Join-Path $record 'provider-callback-review.json'
[IO.File]::WriteAllText($reviewPath, ($review | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $reviewPath
