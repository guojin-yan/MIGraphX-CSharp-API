[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })][string] $RecordDirectory,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CorePackagePath,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $SourceSha,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string] $CoreSha256,
    [Parameter()][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $PrebuiltProbePath
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
function Test-NumericVector([object[]] $Actual, [double[]] $Expected) {
    if ($Actual.Count -ne $Expected.Count) { return $false }
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        if ([double]$Actual[$index] -ne $Expected[$index]) { return $false }
    }
    return $true
}
function Resolve-ManifestPath([string] $Path) {
    $candidate = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $record $Path }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Artifact manifest path is missing: $Path" }
    $resolved = (Resolve-Path -LiteralPath $candidate).Path
    if (-not $resolved.StartsWith($recordRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Artifact manifest path escapes evidence record: $Path" }
    return [IO.Path]::GetFullPath($resolved)
}

$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
$metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
$probeHost = if ($null -ne $metadata.PSObject.Properties['probeHost'] -and -not [string]::IsNullOrWhiteSpace($metadata.probeHost)) { $metadata.probeHost } else { 'dotnet-run' }
if ($result.evidence -ne 'runtime-candidate-executed-review-required' -or
    $result.sourceSha -ne $SourceSha -or $result.expectedVersion -ne '0.0.0' -or
    $result.operationName -ne 'm12_runtime_provider_callback_probe' -or
    $result.registrationState -ne 'registered' -or
    $result.promotionState -ne 'not-requested') {
    throw 'Provider callback result identity or non-promotion boundary is invalid.'
}
if ($metadata.sourceSha -ne $SourceSha -or $metadata.version -ne '0.0.0' -or
    $metadata.probeExitCode -ne 0 -or $metadata.promotionRequested -ne $false -or
    ($probeHost -ne 'dotnet-run' -and $probeHost -ne 'self-contained-linux-x64')) {
    throw 'Provider callback probe metadata is invalid or requests promotion.'
}
if ($metadata.providerFixture -ne $result.providerFixture -or
    ($metadata.providerFixture -ne 'none' -and $metadata.providerFixture -ne 'fake-native-provider-dispatch')) {
    throw 'Provider callback fixture identity is invalid.'
}
switch ($metadata.probeKind) {
    'provider-callback-invocation' {
        if ($result.probeKind -ne $metadata.probeKind -or
            $result.state -ne 'callback-invoked-controlled-rejection' -or
            ($result.graphState -ne 'instruction-created' -and $result.graphState -ne 'compiled' -and $result.graphState -ne 'provider-dispatch-rejected') -or
            $result.controlledFailure -ne $true -or
            $result.callbackInvocations.computeShape -le 0 -or $result.nativeFailureStatus -ne 4 -or
            $metadata.controlledRejection -ne $true -or $metadata.numericalOutputRequested -ne $false) {
            throw 'Provider callback result did not capture the required controlled invocation boundary.'
        }
        $numericalOutputMatched = $false
    }
    'provider-custom-op-numerical-output' {
        $actualMatches = Test-NumericVector @($result.actualOutput) @(1.25, 0.0, 3.0, 10.0)
        $expectedMatches = Test-NumericVector @($result.expectedOutput) @(1.25, 0.0, 3.0, 10.0)
        $inputMatches = Test-NumericVector @($result.inputValues) @(0.25, -1.0, 2.0, 9.0)
        if ($result.probeKind -ne $metadata.probeKind -or
            $result.state -ne 'callback-invoked-reference-matched' -or
            $result.graphState -ne 'executed' -or
            $result.controlledFailure -ne $false -or
            $result.callbackInvocations.computeShape -le 0 -or $result.callbackInvocations.compute -le 0 -or
            $result.callbackInvocations.runsOnOffloadTarget -le 0 -or $result.runsOnOffloadTarget -ne $false -or
            $result.numericalOutputMatched -ne $true -or
            -not $inputMatches -or -not $expectedMatches -or -not $actualMatches -or
            $metadata.controlledRejection -ne $false -or $metadata.numericalOutputRequested -ne $true) {
            throw 'Provider custom-op result did not match the required float32 numerical reference.'
        }
        $numericalOutputMatched = $true
    }
    default { throw "Unrecognized provider callback probe kind: $($metadata.probeKind)" }
}
if ((Get-Sha256 $CorePackagePath) -ne $CoreSha256) { throw 'Core package hash mismatch.' }
if (-not ((Get-Content -LiteralPath $identityPath) -contains "sourceSha=$SourceSha")) { throw 'Source identity is missing.' }
if (-not ((Get-Content -LiteralPath $identityPath) -contains "coreSha256=$CoreSha256")) { throw 'Core package identity is missing.' }
$prebuiltProbeSha256 = ''
if ($probeHost -eq 'self-contained-linux-x64') {
    if ([string]::IsNullOrWhiteSpace($PrebuiltProbePath)) { throw 'A locally preserved prebuilt probe is required for source-host review.' }
    $prebuiltProbeSha256 = Get-Sha256 $PrebuiltProbePath
    if (-not ((Get-Content -LiteralPath $identityPath) -contains "probeSha256=$prebuiltProbeSha256")) {
        throw 'Prebuilt probe identity is missing or does not match the preserved binary.'
    }
}

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
    probeHost = $probeHost
    prebuiltProbeSha256 = $prebuiltProbeSha256
    callbackInvocationObserved = $true
    probeKind = $metadata.probeKind
    controlledFailure = [bool]$result.controlledFailure
    numericalOutputMatched = $numericalOutputMatched
    nativeFailureStatus = $result.nativeFailureStatus
    artifactHashesRecomputed = $true
    verifiedUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$reviewPath = Join-Path $record 'provider-callback-review.json'
[IO.File]::WriteAllText($reviewPath, ($review | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $reviewPath
