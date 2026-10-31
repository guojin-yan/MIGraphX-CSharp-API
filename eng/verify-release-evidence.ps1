[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $IndexPath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CorePackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $AdapterPackagePath,
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string] $Version = '0.0.0',
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $RepositoryCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'release-evidence.psm1') -Force
$indexPath = (Resolve-Path -LiteralPath $IndexPath).Path
$directory = Split-Path -Parent $indexPath
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json
if ($index.schemaVersion -ne '1.0.0' -or $index.version -ne $Version -or $index.repositoryCommit -ne $RepositoryCommit -or
    $index.evidenceLevel -ne 'release-candidate-local' -or $index.publicationAuthorized -ne $false) {
    throw 'Release evidence identity or status is incorrect.'
}

$actualPackages = @(
    Get-ReleasePackageIdentity -Path $CorePackagePath
    Get-ReleasePackageIdentity -Path $AdapterPackagePath
)
Assert-ReleasePackageIdentity -Package $actualPackages[0] -Id 'JYPPX.ROCm.MIGraphX.CSharp.API' -Version $Version -RepositoryCommit $RepositoryCommit
Assert-ReleasePackageIdentity -Package $actualPackages[1] -Id 'JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop' -Version $Version -RepositoryCommit $RepositoryCommit
$dependency = Get-ReleaseDependencyIdentity

function Assert-PackageRecords($Records, [switch] $IncludeFiles) {
    if (@($Records).Count -ne 2) { throw 'Release evidence must contain exactly the core and adapter packages.' }
    foreach ($actual in $actualPackages) {
        $matches = @($Records | Where-Object { $_.id -ceq $actual.id })
        if ($matches.Count -ne 1) { throw "Release evidence package mismatch for $($actual.id)." }
        $record = $matches[0]
        foreach ($field in @('version', 'size', 'sha256', 'normalizedContentSha256')) {
            if ($record.$field -cne $actual.$field) { throw "Release evidence package $field mismatch for $($actual.id)." }
        }
        if ($IncludeFiles) {
            if ($record.repositoryCommit -cne $RepositoryCommit -or @($record.files).Count -ne $actual.files.Count) {
                throw "Release manifest source or file count mismatch for $($actual.id)."
            }
            foreach ($file in $actual.files) {
                $match = @($record.files | Where-Object { $_.path -ceq $file.path })
                if ($match.Count -ne 1 -or $match[0].size -ne $file.size -or $match[0].sha256 -cne $file.sha256) {
                    throw "Release manifest file mismatch: $($actual.id)/$($file.path)"
                }
            }
        }
    }
}
function Assert-DependencyRecord($Record) {
    foreach ($field in @('id', 'version', 'repositoryCommit', 'sha256')) {
        if ($Record.$field -cne $dependency.$field) { throw "Release dependency $field mismatch." }
    }
}

Assert-PackageRecords $index.packages
Assert-DependencyRecord $index.dependency
if (@($index.evidence).Count -ne 4) { throw 'Release index must reference exactly four evidence files.' }
$evidenceFiles = @{}
foreach ($evidence in $index.evidence) {
    if ($evidence.path -match '[/\\:]' -or $evidence.path -notmatch '^[^.].*\.json$') {
        throw "Evidence path must be a JSON filename in the index directory: $($evidence.path)"
    }
    $role = switch -CaseSensitive ($evidence.path) {
        { $_ -in @('managed-package-manifest.json', 'm8-managed-package-manifest.json') } { 'manifest'; break }
        { $_ -in @('managed.sbom.cdx.json', 'm8-managed.sbom.cdx.json') } { 'sbom'; break }
        { $_ -in @('managed.provenance.json', 'm8-managed.provenance.json') } { 'provenance'; break }
        default { 'scan' }
    }
    if ($evidenceFiles.ContainsKey($role) -or $evidence.path -in @('release-evidence.json', 'm8-release-evidence.json')) {
        throw "Duplicate or invalid release evidence role: $role"
    }
    $path = Join-Path $directory $evidence.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-ReleaseSha256 -Path $path) -cne $evidence.sha256) {
        throw "Release evidence file mismatch: $($evidence.path)"
    }
    $evidenceFiles[$role] = $path
}
foreach ($role in @('manifest', 'sbom', 'provenance', 'scan')) {
    if (-not $evidenceFiles.ContainsKey($role)) { throw "Release evidence is missing $role." }
}

$manifest = Get-Content -Raw -LiteralPath $evidenceFiles.manifest | ConvertFrom-Json
if ($manifest.schemaVersion -ne '1.0.0' -or $manifest.version -cne $Version -or $manifest.source.commit -cne $RepositoryCommit -or
    $manifest.source.repository -cne 'https://github.com/guojin-yan/MIGraphX-CSharp-API' -or @($manifest.resolvedDependencies).Count -ne 1) {
    throw 'Release manifest identity is incorrect.'
}
Assert-PackageRecords $manifest.packages -IncludeFiles
Assert-DependencyRecord $manifest.resolvedDependencies[0]
$scan = Get-Content -Raw -LiteralPath $evidenceFiles.scan | ConvertFrom-Json
Assert-ReleaseVulnerabilityScan -Scan $scan -Version $Version -RepositoryCommit $RepositoryCommit

$sbom = Get-Content -Raw -LiteralPath $evidenceFiles.sbom | ConvertFrom-Json
$fileCount = ($actualPackages | ForEach-Object { $_.files.Count } | Measure-Object -Sum).Sum
if ($sbom.bomFormat -cne 'CycloneDX' -or $sbom.specVersion -cne '1.5' -or $sbom.metadata.component.version -cne $Version -or
    @($sbom.components).Count -ne (3 + $fileCount) -or @($sbom.dependencies).Count -ne 3) {
    throw 'Managed product SBOM is incomplete.'
}
foreach ($package in @($actualPackages) + @($dependency)) {
    $ref = "pkg:nuget/$($package.id)@$($package.version)"
    $library = @($sbom.components | Where-Object { $_.'bom-ref' -ceq $ref })
    if ($library.Count -ne 1 -or $library[0].type -cne 'library' -or $library[0].name -cne $package.id -or
        $library[0].version -cne $package.version -or $library[0].purl -cne $ref -or @($library[0].hashes).Count -ne 1 -or
        $library[0].hashes[0].alg -cne 'SHA-256' -or $library[0].hashes[0].content -cne $package.sha256 -or
        @($library[0].properties | Where-Object { $_.name -ceq 'jyppx:repository-commit' -and $_.value -ceq $package.repositoryCommit }).Count -ne 1) {
        throw "SBOM package identity mismatch: $ref"
    }
    $edge = @($sbom.dependencies | Where-Object { $_.ref -ceq $ref })
    $expectedDependencies = if ($package.id -ceq $actualPackages[1].id) {
        @("pkg:nuget/$($actualPackages[0].id)@$Version", "pkg:nuget/$($dependency.id)@$($dependency.version)")
    } else { @() }
    if ($edge.Count -ne 1 -or @($edge[0].dependsOn).Count -ne @($expectedDependencies).Count -or
        @($edge[0].dependsOn | Sort-Object -Unique).Count -ne @($expectedDependencies).Count -or
        @($edge[0].dependsOn | Where-Object { $_ -cnotin $expectedDependencies }).Count -ne 0) {
        throw "SBOM dependency mismatch: $ref"
    }
}
foreach ($package in $actualPackages) {
    foreach ($file in $package.files) {
        $ref = "pkg:nuget/$($package.id)@$Version#$($file.path)"
        $component = @($sbom.components | Where-Object { $_.'bom-ref' -ceq $ref })
        if ($component.Count -ne 1 -or $component[0].type -cne 'file' -or $component[0].name -cne $file.path -or
            @($component[0].hashes).Count -ne 1 -or $component[0].hashes[0].alg -cne 'SHA-256' -or $component[0].hashes[0].content -cne $file.sha256) {
            throw "SBOM file mismatch: $ref"
        }
    }
}

$provenance = Get-Content -Raw -LiteralPath $evidenceFiles.provenance | ConvertFrom-Json
if ($provenance._type -cne 'https://in-toto.io/Statement/v1' -or $provenance.predicateType -cne 'https://slsa.dev/provenance/v1' -or
    $provenance.predicate.runDetails.metadata.publicationAuthorized -ne $false -or $provenance.predicate.runDetails.metadata.signed -ne $false -or
    $provenance.predicate.runDetails.metadata.localOnly -ne $true -or
    $provenance.predicate.buildDefinition.externalParameters.version -cne $Version -or
    $provenance.predicate.buildDefinition.externalParameters.repositoryCommit -cne $RepositoryCommit -or @($provenance.subject).Count -ne 2) {
    throw 'Managed provenance boundary is incorrect.'
}
foreach ($package in $actualPackages) {
    $subject = @($provenance.subject | Where-Object { $_.name -ceq "$($package.id).$Version.nupkg" })
    if ($subject.Count -ne 1 -or $subject[0].digest.sha256 -cne $package.sha256) { throw 'Provenance subject mismatch.' }
}
$dependencies = @($provenance.predicate.buildDefinition.resolvedDependencies)
if ($dependencies.Count -ne 1 -or $dependencies[0].uri -cne "pkg:nuget/$($dependency.id)@$($dependency.version)" -or
    $dependencies[0].digest.sha256 -cne $dependency.sha256) { throw 'Provenance dependency mismatch.' }
$byproducts = @($provenance.predicate.runDetails.byproducts)
if ($byproducts.Count -ne 1 -or $byproducts[0].name -cne 'vulnerability-scan' -or
    $byproducts[0].digest.sha256 -cne (Get-ReleaseSha256 -Path $evidenceFiles.scan)) { throw 'Provenance scan digest mismatch.' }
Write-Output "Managed release evidence verified: $indexPath"
