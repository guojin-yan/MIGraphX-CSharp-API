[CmdletBinding()]
param(
    [string] $Manifest = 'nuget/runtime-manifests/linux-x64.json',
    [switch] $Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
Import-Module (Join-Path $PSScriptRoot 'runtime-manifest.psm1') -Force
$manifestPath = if ([IO.Path]::IsPathRooted($Manifest)) { [IO.Path]::GetFullPath($Manifest) } else { [IO.Path]::GetFullPath((Join-Path $root $Manifest)) }
$manifestInfo = Get-MIGraphXRuntimeManifest $manifestPath
$runtime = $manifestInfo.Value
Assert-MIGraphXRuntimeManifest $runtime -AllowPendingMetadata

function ConvertTo-JsonText([object] $Value) {
    return (($Value | ConvertTo-Json -Depth 40) -replace "`r?`n", "`n") + "`n"
}

function Write-Or-Check([string] $Path, [string] $Text, [string] $Name) {
    if ($Check) {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Name is missing: $Path" }
        if ((Get-Content -Raw -LiteralPath $Path) -cne $Text) { throw "$Name is stale. Run eng/generate-runtime-metadata.ps1." }
        return
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

$licenseByPackage = @{}
foreach ($license in @($runtime.licenses)) { $licenseByPackage[[string]$license.sourcePackage] = $license }
$components = [Collections.Generic.List[object]]::new()
$runtimeRef = "pkg:nuget/$($runtime.packageId)@$($runtime.packageVersion)"
$hipRef = "pkg:nuget/$($runtime.topology.runtimeDependency.id)@7.2.1"
$components.Add([ordered]@{
    type = 'library'; 'bom-ref' = $hipRef; name = $runtime.topology.runtimeDependency.id; version = '7.2.1'
    properties = @([ordered]@{ name = 'migraphx:role'; value = 'exact-runtime-dependency' })
})
foreach ($package in @($runtime.packages | Sort-Object name)) {
    $component = [ordered]@{
        type = 'library'
        'bom-ref' = "pkg:deb/amd/$($package.name)@$($package.version)?arch=amd64"
        name = $package.name
        version = $package.version
        hashes = @([ordered]@{ alg = 'SHA-256'; content = $package.sha256 })
        properties = @(
            [ordered]@{ name = 'migraphx:role'; value = $package.role },
            [ordered]@{ name = 'migraphx:acquisition'; value = $package.acquisition },
            [ordered]@{ name = 'migraphx:source-url'; value = $package.url },
            [ordered]@{ name = 'migraphx:source-size'; value = [string]$package.size },
            [ordered]@{ name = 'migraphx:license-inventory'; value = if ($licenseByPackage.ContainsKey([string]$package.name)) { 'recorded' } else { 'pending' } }
        )
    }
    if ($licenseByPackage.ContainsKey([string]$package.name)) {
        $component.licenses = @([ordered]@{ license = [ordered]@{ id = $licenseByPackage[[string]$package.name].expression } })
    }
    $components.Add($component)
}
foreach ($file in @($runtime.files | Sort-Object path)) {
    $properties = [Collections.Generic.List[object]]::new()
    $properties.Add([ordered]@{ name = 'migraphx:package-path'; value = $file.path })
    $properties.Add([ordered]@{ name = 'migraphx:source-package'; value = $file.sourcePackage })
    if ($file.ContainsKey('aliasFor')) { $properties.Add([ordered]@{ name = 'migraphx:alias-for'; value = $file.aliasFor }) }
    else { $properties.Add([ordered]@{ name = 'migraphx:soname'; value = $file.soname }) }
    $components.Add([ordered]@{
        type = 'file'; 'bom-ref' = "native:$($file.path)"; name = [IO.Path]::GetFileName($file.path)
        hashes = @([ordered]@{ alg = 'SHA-256'; content = $file.sha256 })
        properties = @($properties)
    })
}
$dependencies = [Collections.Generic.List[object]]::new()
$runtimeDependencies = @($runtime.packages | Where-Object role -in @('migraphx-root', 'incremental-provider') | Sort-Object name | ForEach-Object { "pkg:deb/amd/$($_.name)@$($_.version)?arch=amd64" }) + $hipRef
$dependencies.Add([ordered]@{ ref = $runtimeRef; dependsOn = $runtimeDependencies })
foreach ($file in @($runtime.files | Where-Object { -not $_.ContainsKey('aliasFor') } | Sort-Object path)) {
    $nativeDependencies = @()
    foreach ($needed in @($file.needed | Sort-Object)) {
        $match = @($runtime.files | Where-Object { -not $_.ContainsKey('aliasFor') -and $_.soname -eq $needed } | Select-Object -First 1)
        if ($match.Count -eq 1) { $nativeDependencies += "native:$($match[0].path)" }
    }
    $dependencies.Add([ordered]@{ ref = "native:$($file.path)"; dependsOn = $nativeDependencies })
}
$sbom = [ordered]@{
    bomFormat = 'CycloneDX'
    specVersion = '1.5'
    version = 1
    metadata = [ordered]@{
        component = [ordered]@{ type = 'library'; 'bom-ref' = $runtimeRef; name = $runtime.packageId; version = $runtime.packageVersion }
        properties = @(
            [ordered]@{ name = 'migraphx:technical-status'; value = $runtime.technicalStatus },
            [ordered]@{ name = 'migraphx:evidence-boundary'; value = 'Deferred inventory only; no candidate nupkg or package-only runtime claim.' }
        )
    }
    components = @($components)
    dependencies = @($dependencies)
}
$sbomText = ConvertTo-JsonText $sbom
$sbomPath = Join-Path $root $runtime.metadata.sbom.path
if ($Check) {
    Write-Or-Check $sbomPath $sbomText 'CycloneDX SBOM'
    $sbomHash = Get-MIGraphXSha256 $sbomPath
    if ($runtime.metadata.sbom.sha256 -ne $sbomHash) { throw 'Manifest SBOM hash is stale.' }
} else {
    Write-Or-Check $sbomPath $sbomText 'CycloneDX SBOM'
    $sbomHash = Get-MIGraphXSha256 $sbomPath
    $runtime.metadata.sbom.sha256 = $sbomHash
}

$closurePath = Join-Path $root $runtime.closure.path
$closureHash = Get-MIGraphXSha256 $closurePath
$runtime.closure.sha256 = $closureHash
$runtime.metadata.declaredContentDigestSha256 = Get-MIGraphXDeclaredContentDigest $runtime $sbomHash

if (-not $Check) {
    [IO.File]::WriteAllText($manifestPath, (ConvertTo-JsonText $runtime), [Text.UTF8Encoding]::new($false))
    $manifestInfo = Get-MIGraphXRuntimeManifest $manifestPath
    $runtime = $manifestInfo.Value
}
Assert-MIGraphXRuntimeManifest $runtime
$manifestHash = Get-MIGraphXSha256 $manifestPath
$provenance = [ordered]@{
    schemaVersion = 1
    predicateType = 'https://slsa.dev/provenance/v1'
    technicalStatus = $runtime.technicalStatus
    publishable = $false
    releaseAuthorized = $false
    source = [ordered]@{
        repository = 'https://github.com/guojin-yan/MIGraphX-CSharp-API.git'
        developmentBaselineCommit = $runtime.verification.baselineCommit
        finalCommitBinding = 'external receipt required after the clean M7 commit'
        upstreamRepository = 'https://github.com/ROCm/AMDMIGraphX'
        upstreamTag = $runtime.source.upstreamTag
        upstreamCommit = $runtime.source.upstreamCommit
    }
    inputs = [ordered]@{
        manifest = [ordered]@{ path = 'nuget/runtime-manifests/linux-x64.json'; sha256 = $manifestHash }
        closure = [ordered]@{ path = $runtime.closure.path; sha256 = $closureHash }
        sbom = [ordered]@{ path = $runtime.metadata.sbom.path; sha256 = $sbomHash }
        inRelease = [ordered]@{ url = $runtime.source.inReleaseUrl; sha256 = $runtime.source.inReleaseSha256 }
        packagesIndex = [ordered]@{ url = $runtime.source.packagesIndexUrl; sha256 = $runtime.source.packagesIndexSha256 }
        rootSourcePackage = [ordered]@{ name = 'migraphx-rpath7.2.1'; sha256 = (@($runtime.packages | Where-Object name -eq 'migraphx-rpath7.2.1')[0].sha256) }
    }
    payload = [ordered]@{
        declaredContentDigestSha256 = $runtime.metadata.declaredContentDigestSha256
        stagingDigestSha256 = $null
        candidateNupkgSha256 = $null
        nativeFamilyFingerprint = $null
    }
    verification = $runtime.verification
    blockers = $runtime.blockers
}
$provenanceText = ConvertTo-JsonText $provenance
$provenancePath = Join-Path $root $runtime.metadata.provenance.path
Write-Or-Check $provenancePath $provenanceText 'runtime provenance'

Write-Host "Runtime metadata $($(if ($Check) { 'verification' } else { 'generation' })) passed: SBOM=$sbomHash content=$($runtime.metadata.declaredContentDigestSha256)"
