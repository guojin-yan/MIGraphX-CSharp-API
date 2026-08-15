[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CorePackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $AdapterPackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $HipSharpPackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $VulnerabilityScanPath,
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string] $Version = '0.9.0-rc.1',
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $RepositoryCommit,
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Import-Module (Join-Path $PSScriptRoot 'release-evidence.psm1') -Force
$root = Get-RepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "artifacts\release-evidence\$Version\$RepositoryCommit"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$core = Get-ReleasePackageIdentity -Path $CorePackagePath
$adapter = Get-ReleasePackageIdentity -Path $AdapterPackagePath
$hip = Get-ReleasePackageIdentity -Path $HipSharpPackagePath
$expected = @(
    @($core, 'JYPPX.ROCm.MIGraphX.CSharp.API', $Version, $RepositoryCommit),
    @($adapter, 'JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop', $Version, $RepositoryCommit),
    @($hip, 'JYPPX.ROCm.HIP.CSharp.API', '0.9.1', '81d124d6a1598680c83c0b398db4d38d181929de')
)
foreach ($item in $expected) {
    if ($item[0].id -ne $item[1] -or $item[0].version -ne $item[2] -or $item[0].repositoryCommit -ne $item[3]) {
        throw "Package identity mismatch for $($item[1])."
    }
}
if ($hip.sha256 -ne 'e71398538d7ff5db91c018cac3a2ff57c4d89e71aa77b50942182bd90a2a5fd2') {
    throw "HipSharp package hash mismatch: $($hip.sha256)"
}

$manifestPath = Join-Path $OutputDirectory 'm8-managed-package-manifest.json'
$sbomPath = Join-Path $OutputDirectory 'm8-managed.sbom.cdx.json'
$provenancePath = Join-Path $OutputDirectory 'm8-managed.provenance.json'
$indexPath = Join-Path $OutputDirectory 'm8-release-evidence.json'
$inputScanPath = (Resolve-Path -LiteralPath $VulnerabilityScanPath).Path
$scanPath = Join-Path $OutputDirectory ([IO.Path]::GetFileName($inputScanPath))
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if (-not [IO.Path]::GetFullPath($inputScanPath).Equals([IO.Path]::GetFullPath($scanPath), [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $inputScanPath -Destination $scanPath -Force
}

$manifestPackages = @(@($core, $adapter) | ForEach-Object {
    [ordered]@{
        id = $_.id; version = $_.version; size = $_.size; sha256 = $_.sha256
        normalizedContentSha256 = $_.normalizedContentSha256; repositoryCommit = $_.repositoryCommit
        license = $_.license; files = $_.files
    }
})
$manifest = [ordered]@{
    schemaVersion = '1.0.0'
    scope = 'M8 managed release candidate; no native payload'
    source = [ordered]@{ repository = 'https://github.com/guojin-yan/MIGraphX-CSharp-API'; commit = $RepositoryCommit }
    version = $Version
    packageIdentityPolicy = 'NuGet ZIP SHA-256 and normalized content SHA-256 are distinct identities.'
    packages = $manifestPackages
    resolvedDependencies = @([ordered]@{
        id = $hip.id; version = $hip.version; sha256 = $hip.sha256
        repositoryCommit = $hip.repositoryCommit; license = $hip.license
    })
    dependencyPolicy = [ordered]@{
        core = 'no runtime NuGet dependencies'
        adapter = "exact [${Version}] core and exact [0.9.1] HipSharp dependencies in every TFM group"
        native = 'system-native; no native package is distributed'
    }
    symbolSourcePolicy = 'RC nupkgs omit PDB, source, and snupkg; repository commit and deterministic PathMap identify source.'
}
Write-ReleaseJson -Value $manifest -Path $manifestPath

$components = @()
foreach ($package in @($core, $adapter, $hip)) {
    $packageRef = "pkg:nuget/$($package.id)@$($package.version)"
    $components += [ordered]@{
        type = 'library'; 'bom-ref' = $packageRef; group = 'JYPPX.ROCm'; name = $package.id; version = $package.version
        hashes = @([ordered]@{ alg = 'SHA-256'; content = $package.sha256 })
        licenses = @([ordered]@{ license = [ordered]@{ id = 'Apache-2.0' } })
        purl = $packageRef
        properties = @(
            [ordered]@{ name = 'jyppx:normalized-content-sha256'; value = $package.normalizedContentSha256 },
            [ordered]@{ name = 'jyppx:repository-commit'; value = $package.repositoryCommit }
        )
    }
    if ($package -ne $hip) {
        foreach ($file in $package.files) {
            $components += [ordered]@{
                type = 'file'; 'bom-ref' = "$packageRef#$($file.path)"; name = $file.path
                hashes = @([ordered]@{ alg = 'SHA-256'; content = $file.sha256 })
                properties = @(
                    [ordered]@{ name = 'jyppx:package'; value = $package.id },
                    [ordered]@{ name = 'jyppx:size'; value = [string] $file.size }
                )
            }
        }
    }
}
$sbom = [ordered]@{
    bomFormat = 'CycloneDX'; specVersion = '1.5'; version = 1
    metadata = [ordered]@{
        component = [ordered]@{ type = 'application'; name = 'MIGraphXSharp managed release candidate'; version = $Version }
        properties = @(
            [ordered]@{ name = 'jyppx:evidence-level'; value = 'release-candidate-local' },
            [ordered]@{ name = 'jyppx:native-distribution'; value = 'system-native/not-applicable' }
        )
    }
    components = $components
    dependencies = @(
        [ordered]@{ ref = "pkg:nuget/$($core.id)@$($core.version)"; dependsOn = @() },
        [ordered]@{ ref = "pkg:nuget/$($adapter.id)@$($adapter.version)"; dependsOn = @("pkg:nuget/$($core.id)@$($core.version)", "pkg:nuget/$($hip.id)@$($hip.version)") },
        [ordered]@{ ref = "pkg:nuget/$($hip.id)@$($hip.version)"; dependsOn = @() }
    )
}
Write-ReleaseJson -Value $sbom -Path $sbomPath

$provenance = [ordered]@{
    _type = 'https://in-toto.io/Statement/v1'
    subject = @(@($core, $adapter) | ForEach-Object { [ordered]@{ name = "$($_.id).$($_.version).nupkg"; digest = [ordered]@{ sha256 = $_.sha256 } } })
    predicateType = 'https://slsa.dev/provenance/v1'
    predicate = [ordered]@{
        buildDefinition = [ordered]@{
            buildType = 'https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/main/docs/design/m8-api-release-readiness.md'
            externalParameters = [ordered]@{ version = $Version; repositoryCommit = $RepositoryCommit; configuration = 'Release' }
            resolvedDependencies = @([ordered]@{ uri = "pkg:nuget/$($hip.id)@$($hip.version)"; digest = [ordered]@{ sha256 = $hip.sha256 } })
        }
        runDetails = [ordered]@{
            builder = [ordered]@{ id = 'MIGraphXSharp/eng/verify-release-candidate.ps1' }
            metadata = [ordered]@{ localOnly = $true; signed = $false; publicationAuthorized = $false }
            byproducts = @([ordered]@{ name = 'vulnerability-scan'; digest = [ordered]@{ sha256 = Get-ReleaseSha256 -Path $scanPath } })
        }
    }
}
Write-ReleaseJson -Value $provenance -Path $provenancePath

$index = [ordered]@{
    schemaVersion = '1.0.0'; version = $Version; repositoryCommit = $RepositoryCommit
    evidenceLevel = 'release-candidate-local'; publicationAuthorized = $false
    packages = @(@($core, $adapter) | ForEach-Object { [ordered]@{
        id = $_.id; version = $_.version; size = $_.size; sha256 = $_.sha256; normalizedContentSha256 = $_.normalizedContentSha256
    } })
    dependency = [ordered]@{ id = $hip.id; version = $hip.version; sha256 = $hip.sha256; repositoryCommit = $hip.repositoryCommit }
    evidence = @(
        [ordered]@{ path = [IO.Path]::GetFileName($manifestPath); sha256 = Get-ReleaseSha256 -Path $manifestPath },
        [ordered]@{ path = [IO.Path]::GetFileName($sbomPath); sha256 = Get-ReleaseSha256 -Path $sbomPath },
        [ordered]@{ path = [IO.Path]::GetFileName($provenancePath); sha256 = Get-ReleaseSha256 -Path $provenancePath },
        [ordered]@{ path = [IO.Path]::GetFileName($scanPath); sha256 = Get-ReleaseSha256 -Path $scanPath }
    )
}
Write-ReleaseJson -Value $index -Path $indexPath
Write-Output $indexPath
