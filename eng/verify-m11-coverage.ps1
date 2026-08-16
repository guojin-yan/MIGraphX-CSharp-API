[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$matrixPath = Join-Path $root 'compatibility\m11-runtime-cases.json'
$schemaPath = Join-Path $root 'compatibility\schemas\m11-runtime-cases.schema.json'
$matrixText = Get-Content -Raw -LiteralPath $matrixPath
if (-not ($matrixText | Test-Json -SchemaFile $schemaPath)) { throw 'M11 runtime cases do not match their JSON schema.' }
$matrix = $matrixText | ConvertFrom-Json
if ($matrix.stage -ne 'M11' -or $matrix.candidateVersion -ne '0.9.0-rc.4') { throw 'M11 candidate identity drifted.' }
if ($matrix.authorization.officialFunctionalAuthorized -ne $false -or
    $matrix.authorization.longRunAuthorized -ne $false -or
    $matrix.authorization.timingAuthorized -ne $false -or
    $matrix.authorization.environmentChangesAuthorized -ne $false) {
    throw 'M11 must remain unauthorized until a new Owner decision is recorded.'
}
if ($matrix.thresholds.functional.sessionTimeoutSeconds -ne 1800 -or
    $matrix.thresholds.functional.iterations -ne 3 -or
    $matrix.thresholds.longRun.hostReservationMinutes -ne 300 -or
    $matrix.thresholds.timing.warmups -ne 20 -or
    $matrix.thresholds.timing.measuredIterations -ne 200 -or
    $matrix.thresholds.timing.freshProcesses -ne 5) {
    throw 'M11 functional, long-run, or timing thresholds drifted.'
}
$cases = @($matrix.cases)
if ($cases.Count -lt 20 -or @($cases | Group-Object id | Where-Object Count -ne 1).Count -ne 0) {
    throw 'M11 must contain at least 20 unique runtime cases.'
}
foreach ($area in @('M4', 'M5', 'M6', 'negative', 'restart', 'long-run', 'timing', 'platform')) {
    if (@($cases | Where-Object area -eq $area).Count -eq 0) { throw "M11 case area is missing: $area" }
}
if (@($cases | Where-Object { $_.officialEvidence -notin @('runtime-deferred', 'not-applicable') }).Count -ne 0) {
    throw 'No M11 official case may be runtime-executed before authorization and review.'
}
$windows = @($cases | Where-Object id -eq 'm11-windows-native-policy')
if ($windows.Count -ne 1 -or $windows[0].officialEvidence -ne 'not-applicable' -or $windows[0].localEvidence -ne 'statically-verified') {
    throw 'The fixed-version Windows native policy must be statically closed as not-applicable.'
}

$fixtureOutput = Join-Path $root 'artifacts\models\m11-coverage'
$generated = @(& (Join-Path $PSScriptRoot 'generate-m11-fixtures.ps1') -OutputDirectory $fixtureOutput)
if ($generated.Count -ne 3) { throw 'M11 fixture generator must produce exactly three models.' }
foreach ($fixture in $matrix.fixtures) {
    $actual = @($generated | Where-Object FileName -eq $fixture.fileName)
    if ($actual.Count -ne 1 -or $actual[0].Sha256 -ne $fixture.sha256 -or $actual[0].License -ne $fixture.license) {
        throw "M11 fixture identity drifted: $($fixture.id)"
    }
}

$m10EvidencePath = Join-Path $root 'compatibility\m10-post-build-runtime-evidence.json'
$m10Evidence = Get-Content -Raw -LiteralPath $m10EvidencePath | ConvertFrom-Json
if ($m10Evidence.sourceSha -ne 'e2386dc69e7640f8ff12d95284e56c3f02c87938' -or
    $m10Evidence.candidateVersion -ne '0.9.0-rc.2' -or $m10Evidence.reviewState -ne 'passed' -or
    $m10Evidence.reviewedEvidence -ne 'runtime-executed' -or @($m10Evidence.promotions).Count -ne 4 -or
    @($m10Evidence.promotions | Where-Object status -ne 'runtime-executed').Count -ne 0 -or
    $m10Evidence.retained[0].id -ne 'function:migraphx_shape_equal' -or $m10Evidence.historicalCandidateImmutable -ne $true) {
    throw 'M10 post-build external evidence was not synchronized accurately.'
}

$map = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m10-high-level-api-map.json') | ConvertFrom-Json
if ($map.counts.supported -ne 84 -or $map.counts.planned -ne 107 -or $map.counts.unsupported -ne 1) {
    throw 'M11 must not change the 84/107/1 aggregate API map.'
}
$coreBaseline = Get-Content -LiteralPath (Join-Path $root 'compatibility\managed-public-api.txt')
$adapterBaseline = Get-Content -LiteralPath (Join-Path $root 'compatibility\m6-adapter-public-api.txt')
if (@($coreBaseline | Where-Object { $_.StartsWith('T|', [StringComparison]::Ordinal) }).Count -ne 27 -or
    @($coreBaseline | Where-Object { -not $_.StartsWith('#', [StringComparison]::Ordinal) -and -not $_.StartsWith('T|', [StringComparison]::Ordinal) -and $_.Length -ne 0 }).Count -ne 160 -or
    @($adapterBaseline | Where-Object { $_.StartsWith('T|', [StringComparison]::Ordinal) }).Count -ne 3 -or
    @($adapterBaseline | Where-Object { -not $_.StartsWith('#', [StringComparison]::Ordinal) -and -not $_.StartsWith('T|', [StringComparison]::Ordinal) -and $_.Length -ne 0 }).Count -ne 11) {
    throw 'M11 public API baseline must remain core 27/160 and adapter 3/11.'
}

$probeProject = Get-Content -Raw -LiteralPath (Join-Path $root 'tools\m11-runtime-probe\M11RuntimeProbe.csproj')
foreach ($package in @('JYPPX.ROCm.MIGraphX.CSharp.API', 'JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop', 'JYPPX.ROCm.HIP.CSharp.API')) {
    if (-not $probeProject.Contains("PackageReference Include=`"$package`"", [StringComparison]::Ordinal)) { throw "M11 package-only probe is missing $package." }
}
if ($probeProject.Contains('ProjectReference', [StringComparison]::Ordinal)) { throw 'M11 runtime probe must not use project references.' }
$probeSource = Get-Content -Raw -LiteralPath (Join-Path $root 'tools\m11-runtime-probe\Program.cs')
if ($probeSource.Contains('runtime-executed', [StringComparison]::Ordinal) -or
    -not $probeSource.Contains('runtime-candidate-executed-review-required', [StringComparison]::Ordinal) -or
    -not $probeSource.Contains('m5-cache-fresh-process-hit', [StringComparison]::Ordinal) -or
    -not $probeSource.Contains('m6-device-input-reference', [StringComparison]::Ordinal)) {
    throw 'M11 runner evidence label or required cases are incomplete.'
}
$runScript = Get-Content -Raw -LiteralPath (Join-Path $root 'tools\m11-runtime-probe\run.sh')
foreach ($required in @('source checkout is not detached', 'sha256sum', 'timeout --foreground', 'functional_session_timeout=1800', 'case_timeout=120', 'runtime-candidate-executed-review-required', 'functionalExitCode', 'cacheRestartExitCode')) {
    if (-not $runScript.Contains($required, [StringComparison]::Ordinal)) { throw "M11 runner identity gate is missing: $required" }
}
$reviewScript = Get-Content -Raw -LiteralPath (Join-Path $root 'tools\m11-runtime-probe\review.ps1')
foreach ($required in @('normalizedContentSha256', 'functionalSessionTimeoutSeconds', 'iterationDurationMilliseconds', 'artifactHashesRecomputed', 'sensitiveScanPassed', "reviewedEvidence = 'runtime-executed'")) {
    if (-not $reviewScript.Contains($required, [StringComparison]::Ordinal)) { throw "M11 independent review is missing: $required" }
}
foreach ($path in @(
    'docs\validation\m11-runtime-hardening-plan.md',
    'tools\m11-runtime-probe\M11RuntimeProbe.csproj',
    'tools\m11-runtime-probe\Program.cs',
    'tools\m11-runtime-probe\run.sh',
    'tools\m11-runtime-probe\review.ps1',
    'tools\m11-runtime-probe\README.md'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $path) -PathType Leaf)) { throw "M11 deliverable is missing: $path" }
}

Write-Output "M11 coverage gate passed: $($cases.Count) frozen cases, deterministic fixtures, package-only probe, independent review, M10 promotion sync, Windows policy, and 84/107/1 API closure."
