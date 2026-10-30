[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $IndexPath,
    [Parameter(Mandatory)][string] $CorePackagePath,
    [Parameter(Mandatory)][string] $AdapterPackagePath,
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $RepositoryCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Import-Module (Join-Path $PSScriptRoot 'release-evidence.psm1') -Force
$verify = Join-Path $PSScriptRoot 'verify-release-evidence.ps1'
$arguments = @{
    IndexPath = (Resolve-Path -LiteralPath $IndexPath).Path
    CorePackagePath = (Resolve-Path -LiteralPath $CorePackagePath).Path
    AdapterPackagePath = (Resolve-Path -LiteralPath $AdapterPackagePath).Path
    Version = $Version; RepositoryCommit = $RepositoryCommit
}
# Start with real, valid packages and evidence; never edit the supplied record.
& $verify @arguments
$baseline = Get-Content -Raw -LiteralPath $arguments.IndexPath | ConvertFrom-Json
$baselineDirectory = Split-Path -Parent $arguments.IndexPath
$testRoot = Join-Path (Get-ArtifactsRoot) "release-evidence-tests/$([Guid]::NewGuid().ToString('N'))"
$script:rejections = 0

function Assert-Rejected([string] $Name, [string] $Expected, [scriptblock] $Action) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw "Unexpected failure in ${Name}: $($_.Exception.Message)" }
        $script:rejections++
        Write-Host "Rejected as expected: $Name"
        return
    }
    throw "Invalid release evidence unexpectedly passed: $Name"
}

function Test-EvidenceMutation([string] $Name, [string] $Expected, [scriptblock] $Mutation) {
    $directory = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    foreach ($entry in $baseline.evidence) {
        Copy-Item -LiteralPath (Join-Path $baselineDirectory $entry.path) -Destination $directory
    }
    $index = Get-Content -Raw -LiteralPath $arguments.IndexPath | ConvertFrom-Json
    $caseArguments = $arguments.Clone()
    $caseArguments.IndexPath = Join-Path $directory 'index.json'
    & $Mutation $index $directory $caseArguments
    Write-ReleaseJson -Value $index -Path $caseArguments.IndexPath
    Assert-Rejected $Name $Expected { & $verify @caseArguments }
}

function Update-Evidence($Index, [string] $Directory, [string] $Pattern, [scriptblock] $Mutation) {
    $entry = @($Index.evidence | Where-Object {
        if ($Pattern -eq 'scan') { $_.path -notmatch '(?:package-manifest|sbom\.cdx|provenance)\.json$' }
        else { $_.path -like $Pattern }
    })[0]
    $path = Join-Path $Directory $entry.path
    $value = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    & $Mutation $value
    Write-ReleaseJson -Value $value -Path $path
    # Recompute the outer hash: semantic checks must catch inconsistent records
    # even when someone has regenerated a self-consistent file-hash index.
    $entry.sha256 = Get-ReleaseSha256 -Path $path
}

Test-EvidenceMutation 'wrong-source-commit' 'Package identity mismatch' {
    param($i, $d, $a) $i.repositoryCommit = '0' * 40; $a.RepositoryCommit = $i.repositoryCommit
}
Test-EvidenceMutation 'wrong-package-version' 'Package identity mismatch' {
    param($i, $d, $a) $i.version = '99.0.0'; $a.Version = $i.version
}
Test-EvidenceMutation 'swapped-packages' 'Package identity mismatch' {
    param($i, $d, $a) $a.CorePackagePath = $a.AdapterPackagePath
}
Test-EvidenceMutation 'extra-package' 'exactly the core and adapter' { param($i) $i.packages += $i.packages[0] }
Test-EvidenceMutation 'wrong-dependency' 'Release dependency sha256 mismatch' { param($i) $i.dependency.sha256 = '0' * 64 }
Test-EvidenceMutation 'missing-evidence' 'exactly four evidence files' { param($i) $i.evidence = @($i.evidence | Select-Object -First 3) }
Test-EvidenceMutation 'escaping-path' 'Evidence path must be a JSON filename' { param($i) $i.evidence[0].path = '../outside.json' }
Test-EvidenceMutation 'duplicate-role' 'Duplicate or invalid release evidence role' { param($i) $i.evidence[1] = $i.evidence[0] }
Test-EvidenceMutation 'manifest-source' 'Release manifest identity' {
    param($i, $d) Update-Evidence $i $d '*package-manifest.json' { param($v) $v.source.commit = '0' * 40 }
}
Test-EvidenceMutation 'manifest-file' 'Release manifest file mismatch' {
    param($i, $d) Update-Evidence $i $d '*package-manifest.json' { param($v) $v.packages[0].files[0].sha256 = '0' * 64 }
}
Test-EvidenceMutation 'scan-no-projects' 'Vulnerability scan identity' {
    param($i, $d) Update-Evidence $i $d 'scan' { param($v) $v.projectCount = 0 }
}
Test-EvidenceMutation 'scan-wrong-dependency' 'Exact consumer dependency is missing' {
    param($i, $d) Update-Evidence $i $d 'scan' { param($v) $v.resolvedPackages[0].version = '99.0.0' }
}
Test-EvidenceMutation 'sbom-missing-files' 'Managed product SBOM is incomplete' {
    param($i, $d) Update-Evidence $i $d '*sbom.cdx.json' { param($v) $v.components = @($v.components | Where-Object type -ne 'file') }
}
Test-EvidenceMutation 'sbom-package-hash' 'SBOM package identity mismatch' {
    param($i, $d) Update-Evidence $i $d '*sbom.cdx.json' { param($v) ($v.components | Where-Object type -eq 'library' | Select-Object -First 1).hashes[0].content = '0' * 64 }
}
Test-EvidenceMutation 'sbom-dependency-edge' 'SBOM dependency mismatch' {
    param($i, $d) Update-Evidence $i $d '*sbom.cdx.json' { param($v) ($v.dependencies | Where-Object { @($_.dependsOn).Count -gt 0 }).dependsOn = @() }
}
Test-EvidenceMutation 'provenance-subject' 'Provenance subject mismatch' {
    param($i, $d) Update-Evidence $i $d '*provenance.json' { param($v) $v.subject[0].digest.sha256 = '0' * 64 }
}
Test-EvidenceMutation 'provenance-scan' 'Provenance scan digest mismatch' {
    param($i, $d) Update-Evidence $i $d '*provenance.json' { param($v) $v.predicate.runDetails.byproducts[0].digest.sha256 = '0' * 64 }
}

$consumerPath = Join-Path (Get-RepositoryRoot) 'tests/fixtures/adapter-package-consumer/Consumer.csproj'
$reportText = @{
    version = 1; parameters = '--vulnerable --include-transitive'; sources = @('https://api.nuget.org/v3/index.json')
    projects = @(@{ path = $consumerPath })
} | ConvertTo-Json -Depth 10
# NuGet omits frameworks when the vulnerability query has no findings.
Assert-ReleaseNuGetReport -Report ($reportText | ConvertFrom-Json) -VulnerabilityScan -ProjectPath $consumerPath
foreach ($case in @(
    @('query-no-projects', 'single adapter consumer', { param($r) $r.projects = @() }),
    @('query-no-sources', 'no advisory sources', { param($r) $r.sources = @() }),
    @('query-wrong-project', 'differs from the resolved consumer', { param($r) $r.projects[0].path = Join-Path $testRoot 'other.csproj' }),
    @('query-error', 'contains problems', { param($r) $r.projects[0] | Add-Member problems @('advisory feed unavailable') }),
    @('query-vulnerable', 'contains vulnerabilities', { param($r) $r.projects[0] | Add-Member frameworks @(@{ framework = 'net10.0'; topLevelPackages = @(@{ vulnerabilities = @(@{ severity = 'High' }) }) }) })
)) {
    $report = $reportText | ConvertFrom-Json
    & $case[2] $report
    Assert-Rejected $case[0] $case[1] { Assert-ReleaseNuGetReport -Report $report -VulnerabilityScan -ProjectPath $consumerPath }
}
Write-Output "Release evidence regression checks passed: 2 positive checks, $script:rejections rejected mutations. Output: $testRoot"
