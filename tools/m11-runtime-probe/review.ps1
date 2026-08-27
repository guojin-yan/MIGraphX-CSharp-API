[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })][string] $RecordDirectory,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CorePackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $AdapterPackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $HipSharpPackagePath,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $SourceSha,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+-rc\.\d+$')][string] $Version,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string] $CoreNormalizedSha256,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string] $AdapterNormalizedSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$record = (Resolve-Path -LiteralPath $RecordDirectory).Path
$raw = Join-Path $record 'raw'
$recordRoot = [IO.Path]::GetFullPath($record).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$functionalPath = Join-Path $raw 'm11-functional.json'
$restartPath = Join-Path $raw 'm11-cache-restart.json'
$metadataPath = Join-Path $raw 'run-metadata.json'
$sourcePath = Join-Path $raw 'source-metadata.json'
$hashManifestPath = Join-Path $raw 'artifact-hashes.txt'
$stagePath = Join-Path $raw 'case-stages.jsonl'
foreach ($path in @($functionalPath, $restartPath, $metadataPath, $sourcePath, $hashManifestPath, $stagePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required review input is missing: $path" }
}

function Get-Sha256([string] $Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-PackageIdentity([string] $Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    try {
        $files = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) } | Sort-Object FullName | ForEach-Object {
            $stream = $_.Open()
            try {
                $memory = [IO.MemoryStream]::new()
                try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $memory.Dispose() }
            }
            finally { $stream.Dispose() }
            $memoryHash = [Security.Cryptography.SHA256]::HashData($bytes)
            [pscustomobject]@{ path = $_.FullName.Replace('\', '/'); size = $bytes.Length; sha256 = [Convert]::ToHexString($memoryHash).ToLowerInvariant() }
        })
        $canonical = ($files | ForEach-Object { "$($_.path)`0$($_.size)`0$($_.sha256)`n" }) -join ''
        $normalized = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
        return [pscustomobject]@{ sha256 = Get-Sha256 $Path; normalizedContentSha256 = $normalized }
    }
    finally { $archive.Dispose() }
}

$functional = Get-Content -Raw -LiteralPath $functionalPath | ConvertFrom-Json
$restart = Get-Content -Raw -LiteralPath $restartPath | ConvertFrom-Json
$metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
$source = Get-Content -Raw -LiteralPath $sourcePath | ConvertFrom-Json
foreach ($result in @($functional, $restart)) {
    if ($result.evidence -ne 'runtime-candidate-executed-review-required' -or $result.state -ne 'executed') {
        throw 'Probe output must remain a successful review-required candidate.'
    }
    if ($result.sourceSha -ne $SourceSha) { throw 'Probe source SHA mismatch.' }
    if (@($result.cases | Where-Object state -ne 'passed').Count -ne 0) { throw 'At least one probe case failed.' }
}
if ($metadata.evidence -ne 'runtime-candidate-executed-review-required' -or $metadata.functionalExitCode -ne 0 -or $metadata.cacheRestartExitCode -ne 0) {
    throw 'Runner metadata did not close both bounded functional processes.'
}
if ($metadata.functionalSessionTimeoutSeconds -ne 1800 -or $metadata.caseTimeoutSeconds -ne 120 -or
    $metadata.sessionKillAfterSeconds -ne 10) {
    throw 'Runner timeout boundaries do not match the frozen functional plan.'
}
if ($metadata.gpuRuntimeQueryExecuted -ne $false -or $metadata.caseStageTraceFile -ne 'raw/case-stages.jsonl') {
    throw 'Runner safety or stage-trace metadata drifted.'
}
if ($metadata.longRunExecuted -ne $false -or $metadata.timingExecuted -ne $false -or $metadata.environmentChanged -ne $false) {
    throw 'Unauthorized long-run, timing, or environment changes were recorded.'
}
if ($source.sourceSha -ne $SourceSha -or $source.cleanDetached -ne $true -or $source.version -ne $Version) {
    throw 'Source metadata is not the expected clean detached candidate.'
}

$expectedFunctionalCases = @(
    'm11-registry-before',
    'm4-explicit-lifecycle',
    'm4-file-buffer-reference',
    'm4-multi-output-order-lifetime',
    'm4-dispose-and-input-negatives',
    'm5-static-overrides',
    'm5-dynamic-overrides',
    'm5-invalid-range-name-shape',
    'm5-save-load-recompile',
    'm5-cache-cold-warm-identity',
    'm5-cache-model-options-miss',
    'm5-cache-corruption-rebuild',
    'm5-cache-concurrent-writers',
    'm6-host-async-completion',
    'm6-same-stream-multiple',
    'm6-distinct-stream-isolation',
    'm6-early-dispose-leases',
    'm6-device-input-reference',
    'm6-device-input-validation',
    'm11-registry-after'
)
if (Compare-Object $expectedFunctionalCases @($functional.cases.id)) { throw 'Functional case set mismatch.' }
if (@($restart.cases).Count -ne 1 -or $restart.cases[0].id -ne 'm5-cache-fresh-process-hit') { throw 'Fresh-process cache case mismatch.' }
$repeatedCases = @(
    'm4-explicit-lifecycle',
    'm4-file-buffer-reference',
    'm4-multi-output-order-lifetime',
    'm5-static-overrides',
    'm5-dynamic-overrides',
    'm5-save-load-recompile',
    'm5-cache-cold-warm-identity',
    'm6-host-async-completion',
    'm6-same-stream-multiple',
    'm6-distinct-stream-isolation',
    'm6-early-dispose-leases',
    'm6-device-input-reference'
)
foreach ($case in $functional.cases) {
    $expectedIterations = if ($case.id -in $repeatedCases) { 3 } else { 1 }
    if ($case.detail.iterations -ne $expectedIterations -or @($case.detail.iterationDurationMilliseconds).Count -ne $expectedIterations) {
        throw "Functional iteration count mismatch: $($case.id)"
    }
}
if ($restart.cases[0].detail.iterations -ne 1) { throw 'Fresh-process cache iteration count mismatch.' }

$stageEntries = @(Get-Content -LiteralPath $stagePath | ForEach-Object { $_ | ConvertFrom-Json })
if ($stageEntries.Count -eq 0 -or
    @($stageEntries | Where-Object schemaVersion -ne '1.0.0').Count -ne 0 -or
    @($stageEntries | Where-Object caseId -ne 'm4-file-buffer-reference').Count -ne 0 -or
    @($stageEntries | Where-Object state -notin @('started', 'completed')).Count -ne 0 -or
    @($stageEntries | Where-Object exception).Count -ne 0) {
    throw 'File/buffer stage trace contains an invalid entry.'
}
for ($index = 0; $index -lt $stageEntries.Count; $index++) {
    if ([long]$stageEntries[$index].sequence -ne ($index + 1)) {
        throw 'File/buffer stage trace sequence is not complete and ordered.'
    }
}
$requiredStages = @(
    'iteration',
    'file.options', 'file.parse', 'file.shape', 'file.target', 'file.compile-options', 'file.compile',
    'file.compile-options.dispose', 'file.target.dispose', 'file.argument', 'file.parameter-map',
    'file.parameter-map-add', 'file.run', 'file.output-count', 'file.readback', 'file.run.dispose',
    'file.parameter-map.dispose', 'file.argument.dispose', 'file.parse.dispose', 'file.options.dispose',
    'buffer.options', 'buffer.parse', 'buffer.shape', 'buffer.target', 'buffer.compile-options', 'buffer.compile',
    'buffer.compile-options.dispose', 'buffer.target.dispose', 'buffer.argument', 'buffer.parameter-map',
    'buffer.parameter-map-add', 'buffer.run', 'buffer.output-count', 'buffer.readback', 'buffer.run.dispose',
    'buffer.parameter-map.dispose', 'buffer.argument.dispose', 'buffer.parse.dispose', 'buffer.options.dispose',
    'reference-check', 'equivalence.file-options', 'equivalence.buffer-options', 'equivalence.file-parse',
    'equivalence.buffer-parse', 'equivalence.compare', 'equivalence.buffer-parse.dispose',
    'equivalence.file-parse.dispose', 'equivalence.buffer-options.dispose', 'equivalence.file-options.dispose'
)
if ($stageEntries.Count -ne ($requiredStages.Count * 2 * 3)) { throw 'File/buffer stage trace contains extra or missing entries.' }
foreach ($iteration in 1..3) {
    $iterationEntries = @($stageEntries | Where-Object iteration -eq $iteration)
    foreach ($stage in $requiredStages) {
        $started = @($iterationEntries | Where-Object { $_.stage -eq $stage -and $_.state -eq 'started' })
        $completed = @($iterationEntries | Where-Object { $_.stage -eq $stage -and $_.state -eq 'completed' })
        if ($started.Count -ne 1 -or $completed.Count -ne 1 -or $started[0].sequence -ge $completed[0].sequence) {
            throw "File/buffer stage trace did not close iteration $iteration stage $stage."
        }
    }
}
$before = @($functional.cases | Where-Object id -eq 'm11-registry-before')[0].detail
$after = @($functional.cases | Where-Object id -eq 'm11-registry-after')[0].detail
if ($before.count -ne $after.count -or $before.orderedJsonSha256 -ne $after.orderedJsonSha256 -or $after.drift -ne $false) {
    throw 'Registry drift check failed.'
}

$coreIdentity = Get-PackageIdentity $CorePackagePath
$adapterIdentity = Get-PackageIdentity $AdapterPackagePath
$hipHash = Get-Sha256 $HipSharpPackagePath
if ($coreIdentity.normalizedContentSha256 -ne $CoreNormalizedSha256 -or $adapterIdentity.normalizedContentSha256 -ne $AdapterNormalizedSha256) {
    throw 'Normalized package identity mismatch.'
}
if ($functional.managedIdentity.packageVersion -ne $Version -or -not $functional.managedIdentity.coreInformationalVersion.Contains($SourceSha, [StringComparison]::Ordinal)) {
    throw 'Executed assembly identity is not bound to the reviewed candidate.'
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
foreach ($line in Get-Content -LiteralPath $hashManifestPath) {
    if ($line -notmatch '^([a-f0-9]{64})\s+(.+)$') { throw "Malformed artifact hash line: $line" }
    $expected = $Matches[1]
    $path = Resolve-ManifestPath $Matches[2]
    if (-not $manifestPaths.Add($path)) { throw "Duplicate artifact manifest path: $path" }
    if ((Get-Sha256 $path) -ne $expected) { $manifestFailures.Add($path) }
}
if ($manifestFailures.Count -ne 0) { throw "Artifact hash mismatches: $($manifestFailures -join ', ')" }
foreach ($requiredPath in @($functionalPath, $restartPath, $metadataPath, $sourcePath, $stagePath)) {
    $resolvedRequiredPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $requiredPath).Path)
    if (-not $manifestPaths.Contains($resolvedRequiredPath)) {
        throw "Artifact manifest is missing required review input: $requiredPath"
    }
}

$sensitivePatterns = @('BEGIN OPENSSH ' + 'PRIVATE KEY', 'BEGIN ' + 'PRIVATE KEY', 'password=', 'token=', 'ssh-rsa ', 'ed25519 ')
$textExtensions = @('.txt', '.json', '.log', '.md', '.ps1', '.sh', '.cs', '.csproj')
$sensitiveFailures = [Collections.Generic.List[string]]::new()
foreach ($path in Get-ChildItem -LiteralPath $record -File -Recurse) {
    if ($path.Extension -notin $textExtensions) { continue }
    $text = [string](Get-Content -Raw -LiteralPath $path.FullName)
    foreach ($pattern in $sensitivePatterns) {
        if ($text.Contains($pattern, [StringComparison]::OrdinalIgnoreCase)) { $sensitiveFailures.Add("$($path.FullName):$pattern") }
    }
}
if ($sensitiveFailures.Count -ne 0) { throw "Sensitive scan failed: $($sensitiveFailures -join ', ')" }

$review = [ordered]@{
    schemaVersion = '1.0.0'
    reviewState = 'passed'
    reviewedEvidence = 'runtime-executed'
    reviewedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    sourceSha = $SourceSha
    version = $Version
    functionalCaseCount = @($functional.cases).Count
    cacheRestartCaseCount = @($restart.cases).Count
    registryCount = $before.count
    registryOrderedJsonSha256 = $before.orderedJsonSha256
    corePackageSha256 = $coreIdentity.sha256
    coreNormalizedContentSha256 = $coreIdentity.normalizedContentSha256
    adapterPackageSha256 = $adapterIdentity.sha256
    adapterNormalizedContentSha256 = $adapterIdentity.normalizedContentSha256
    hipSharpPackageSha256 = $hipHash
    functionalResultSha256 = Get-Sha256 $functionalPath
    cacheRestartResultSha256 = Get-Sha256 $restartPath
    caseStageTraceSha256 = Get-Sha256 $stagePath
    caseStageTraceValidated = $true
    sessionKillAfterSeconds = $metadata.sessionKillAfterSeconds
    artifactHashesRecomputed = $true
    sensitiveScanPassed = $true
    longRunReviewed = $false
    timingReviewed = $false
    windowsNativePolicy = 'not-applicable'
}
$reviewPath = Join-Path $record 'review.json'
[IO.File]::WriteAllText($reviewPath, ($review | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $reviewPath
