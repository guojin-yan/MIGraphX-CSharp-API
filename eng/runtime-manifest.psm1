Set-StrictMode -Version Latest

function Get-MIGraphXSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-MIGraphXHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Value,
        [Parameter(Mandatory = $true)][string] $Name
    )

    if ($Value -notmatch '^[0-9a-f]{64}$') {
        throw "$Name must be a lowercase SHA-256 value."
    }
}

function ConvertTo-MIGraphXRelativePath {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string] $Path)

    $normalized = $Path.Replace('\', '/')
    $segments = $normalized.Split('/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized -ne $normalized.Trim() -or
        $normalized.IndexOf([char]0) -ge 0 -or
        $normalized.StartsWith('/', [StringComparison]::Ordinal) -or
        $normalized -match '^[A-Za-z]:' -or
        $normalized -match '[\x00-\x1f]' -or
        $segments -contains '' -or
        $segments -contains '..' -or
        $segments -contains '.') {
        throw "Runtime manifest path must be non-rooted and traversal-free: $Path"
    }

    return $normalized
}

function Get-MIGraphXRuntimeManifest {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string] $ManifestPath)

    $resolved = (Resolve-Path -LiteralPath $ManifestPath).Path
    $value = Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json -AsHashtable
    return [pscustomobject]@{ Path = $resolved; Value = $value }
}

function Assert-MIGraphXOfficialUrl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Value,
        [Parameter(Mandatory = $true)][string] $Name
    )

    $uri = [Uri]$Value
    if ($uri.Scheme -ne 'https' -or
        -not [string]::Equals($uri.Host, 'repo.radeon.com', [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::IsNullOrEmpty($uri.UserInfo)) {
        throw "$Name must be an HTTPS URL on repo.radeon.com without user information."
    }
}

function Assert-MIGraphXRuntimeManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][hashtable] $Manifest,
        [switch] $RequireCandidate,
        [switch] $AllowPendingMetadata
    )

    $required = @(
        '$schema', 'schemaVersion', 'packageId', 'packageVersion', 'versionSemantics', 'rid',
        'nativeVersion', 'technicalStatus', 'candidateStaged', 'verified',
        'publishAuthorized', 'releaseAuthorized', 'source', 'topology', 'packages',
        'files', 'licenses', 'closure', 'systemDependencies', 'driverBoundary',
        'size', 'metadata', 'verification', 'blockers'
    )
    foreach ($name in $required) {
        if (-not $Manifest.ContainsKey($name)) { throw "Runtime manifest is missing '$name'." }
    }

    if ($Manifest.'$schema' -ne './schema.json') { throw 'Runtime manifest must bind the tracked schema.' }
    if ($Manifest.schemaVersion -ne 2) { throw 'Runtime manifest schemaVersion must be 2.' }
    if ($Manifest.packageId -ne 'JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64') { throw 'Runtime package ID is not the frozen M7 ID.' }
    if ($Manifest.packageVersion -ne '7.2.1' -or $Manifest.rid -ne 'linux-x64') { throw 'Runtime version/RID must remain ROCm 7.2.1 linux-x64.' }
    if ($Manifest.versionSemantics -ne 'ROCm lockstep; MIGraphX native identity is recorded separately') { throw 'Runtime version semantics drifted.' }
    if ($Manifest.technicalStatus -notin @('runtime-deferred', 'runtime-candidate-staged', 'runtime-candidate-executed', 'runtime-executed')) { throw 'Unknown runtime technical status.' }
    foreach ($name in @('candidateStaged', 'verified', 'publishAuthorized', 'releaseAuthorized')) {
        if ($Manifest[$name] -isnot [bool]) { throw "$name must be a Boolean." }
    }
    if ($Manifest.releaseAuthorized -and -not $Manifest.publishAuthorized) { throw 'Release authorization cannot exist without publish authorization.' }
    if ($Manifest.verified -and -not $Manifest.candidateStaged) { throw 'A verified runtime must first be staged as an exact candidate.' }
    if ($Manifest.technicalStatus -eq 'runtime-deferred' -and
        ($Manifest.candidateStaged -or $Manifest.verified -or $Manifest.publishAuthorized -or $Manifest.releaseAuthorized)) {
        throw 'A deferred runtime must keep all packaging and authorization flags false.'
    }
    if ($Manifest.publishAuthorized -or $Manifest.releaseAuthorized) {
        throw 'Tracked source metadata cannot authorize publication or release.'
    }

    $source = $Manifest.source
    foreach ($name in @('repositoryUrl', 'inReleaseUrl', 'packagesIndexUrl', 'signingKeyUrl')) {
        Assert-MIGraphXOfficialUrl ([string]$source[$name]) "source.$name"
    }
    foreach ($name in @('signingKeySha256', 'inReleaseSha256', 'packagesIndexSha256', 'headerSha256')) {
        Assert-MIGraphXHash ([string]$source[$name]) "source.$name"
    }
    if ($source.signingKeyFingerprint -notmatch '^[0-9A-F]{40}$') { throw 'The signing key fingerprint must be 40 uppercase hexadecimal characters.' }
    if ($source.architecture -ne 'amd64' -or $source.distribution -ne 'noble') { throw 'Only the audited Ubuntu Noble amd64 source is accepted.' }

    if ($Manifest.topology.decision -ne 'layered-deferred') { throw 'M7 topology must remain layered-deferred until a new review.' }
    if ($Manifest.topology.runtimeDependency.id -ne 'JYPPX.ROCm.HIP.CSharp.API.Runtime.linux-x64' -or
        $Manifest.topology.runtimeDependency.version -ne '[7.2.1]') {
        throw 'The layered topology must use the exact HipSharp ROCm 7.2.1 runtime dependency.'
    }

    $packageNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $providedSonames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($soname in @($Manifest.topology.runtimeDependency.provides)) { $providedSonames.Add([string]$soname) | Out-Null }
    foreach ($package in @($Manifest.packages)) {
        foreach ($name in @('name', 'version', 'architecture', 'url', 'size', 'sha256', 'role', 'acquisition')) {
            if (-not $package.ContainsKey($name)) { throw "Source package is missing '$name'." }
        }
        if (-not $packageNames.Add([string]$package.name)) { throw "Duplicate source package: $($package.name)" }
        if ($package.architecture -ne 'amd64') { throw "Wrong source architecture for $($package.name)." }
        if ($package.role -notin @('migraphx-root', 'incremental-provider', 'provided-by-exact-hip-runtime')) { throw "Unknown package role for $($package.name)." }
        if ($package.acquisition -notin @('required', 'metadata-only')) { throw "Unknown acquisition policy for $($package.name)." }
        Assert-MIGraphXOfficialUrl ([string]$package.url) "package $($package.name) URL"
        Assert-MIGraphXHash ([string]$package.sha256) "package $($package.name) hash"
        if ([int64]$package.size -le 0) { throw "Source package $($package.name) must have positive size." }
        if ($package.ContainsKey('provides')) {
            foreach ($soname in @($package.provides)) { $providedSonames.Add([string]$soname) | Out-Null }
        }
    }
    if (@($Manifest.packages | Where-Object role -eq 'migraphx-root').Count -ne 1) { throw 'Exactly one MIGraphX root source package is required.' }

    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $canonical = @()
    foreach ($file in @($Manifest.files)) {
        foreach ($name in @('path', 'sourcePath', 'sourcePackage', 'sha256', 'size', 'purpose')) {
            if (-not $file.ContainsKey($name)) { throw "Runtime file is missing '$name'." }
        }
        $path = ConvertTo-MIGraphXRelativePath ([string]$file.path)
        ConvertTo-MIGraphXRelativePath ([string]$file.sourcePath) | Out-Null
        if (-not $path.StartsWith('runtimes/linux-x64/native/', [StringComparison]::Ordinal)) { throw "Runtime file escapes the RID native directory: $path" }
        if ($path -match '(?i)(^|/)(include|cmake|bin|libexec|tests?|models?|cache|artifacts)(/|$)' -or
            $path -match '(?i)\.(a|h|hpp|deb|ddeb|pdb|bc|hsaco|onnx)$') { throw "Forbidden runtime payload path: $path" }
        if (-not $paths.Add($path)) { throw "Duplicate runtime package path: $path" }
        if (-not $packageNames.Contains([string]$file.sourcePackage)) { throw "Runtime file uses an unknown source package: $path" }
        Assert-MIGraphXHash ([string]$file.sha256) "runtime file $path hash"
        if ([int64]$file.size -le 0) { throw "Runtime file $path must have positive size." }
        if ($file.ContainsKey('aliasFor')) {
            ConvertTo-MIGraphXRelativePath ([string]$file.aliasFor) | Out-Null
        } else {
            foreach ($name in @('soname', 'needed', 'rpath', 'elfClass', 'machine')) {
                if (-not $file.ContainsKey($name)) { throw "Canonical runtime file $path is missing '$name'." }
            }
            if ($file.elfClass -ne 'ELF64' -or $file.machine -ne 'x86-64') { throw "Runtime ELF architecture drifted: $path" }
            $canonical += $file
            $providedSonames.Add([string]$file.soname) | Out-Null
        }
    }
    if ($canonical.Count -eq 0) { throw 'At least one canonical ELF is required.' }
    $canonicalByPath = @{}
    foreach ($file in $canonical) { $canonicalByPath[[string]$file.path] = $file }
    foreach ($alias in @($Manifest.files | Where-Object { $_.ContainsKey('aliasFor') })) {
        if (-not $canonicalByPath.ContainsKey([string]$alias.aliasFor)) { throw "Alias target does not exist: $($alias.aliasFor)" }
        $target = $canonicalByPath[[string]$alias.aliasFor]
        if ($alias.sha256 -ne $target.sha256 -or [int64]$alias.size -ne [int64]$target.size -or $alias.sourcePath -ne $target.sourcePath) {
            throw "Alias bytes do not match their canonical payload: $($alias.path)"
        }
    }

    $systemSonames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($dependency in @($Manifest.systemDependencies)) {
        foreach ($name in @('soname', 'ubuntuPackage', 'minimumVersion', 'usage')) {
            if (-not $dependency.ContainsKey($name)) { throw "System dependency is missing '$name'." }
        }
        $systemSonames.Add([string]$dependency.soname) | Out-Null
    }
    foreach ($file in $canonical) {
        foreach ($needed in @($file.needed)) {
            if (-not $providedSonames.Contains([string]$needed) -and -not $systemSonames.Contains([string]$needed)) {
                throw "Unresolved declared dependency '$needed' from $($file.path)."
            }
        }
    }

    $licensePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($license in @($Manifest.licenses)) {
        foreach ($name in @('sourcePackage', 'expression', 'sourcePath', 'packagePath', 'sha256')) {
            if (-not $license.ContainsKey($name)) { throw "License record is missing '$name'." }
        }
        if (-not $packageNames.Contains([string]$license.sourcePackage)) { throw "License uses an unknown source package: $($license.sourcePackage)" }
        ConvertTo-MIGraphXRelativePath ([string]$license.sourcePath) | Out-Null
        $licensePath = ConvertTo-MIGraphXRelativePath ([string]$license.packagePath)
        if (-not $licensePath.StartsWith('licenses/', [StringComparison]::Ordinal) -or -not $licensePaths.Add($licensePath)) { throw "License package path is invalid or duplicated: $licensePath" }
        Assert-MIGraphXHash ([string]$license.sha256) "license $licensePath hash"
    }
    $payloadPackages = @($Manifest.files | ForEach-Object sourcePackage | Sort-Object -Unique)
    $licensedPackages = @($Manifest.licenses | ForEach-Object sourcePackage | Sort-Object -Unique)
    foreach ($payloadPackage in $payloadPackages) {
        if ($licensedPackages -notcontains $payloadPackage) { throw "Payload source package has no license record: $payloadPackage" }
    }

    foreach ($name in @('deviceNodes', 'kernelDriver', 'excludedFromPackage', 'rule')) {
        if (-not $Manifest.driverBoundary.ContainsKey($name) -or @($Manifest.driverBoundary[$name]).Count -eq 0) { throw "driverBoundary.$name is required." }
    }
    $canonicalBytes = [int64](($canonical | Measure-Object size -Sum).Sum)
    $allBytes = [int64](($Manifest.files | Measure-Object size -Sum).Sum)
    $incrementalBytes = [int64](($Manifest.packages | Where-Object { $_.role -in @('migraphx-root', 'incremental-provider') } | Measure-Object size -Sum).Sum)
    if ($canonicalBytes -ne [int64]$Manifest.size.manifestCanonicalBytes -or
        $allBytes -ne [int64]$Manifest.size.manifestAliasMaterializedBytes -or
        $incrementalBytes -ne [int64]$Manifest.size.incrementalSourceArchiveBytes) {
        throw 'Runtime size fields do not match the manifest inventory.'
    }
    if ([int64]$Manifest.size.packageLimitBytes -le 0 -or [int64]$Manifest.size.unpackedLimitBytes -le 0) {
        throw 'Runtime package and unpacked size limits must be positive.'
    }
    if ([int64]$Manifest.size.largestRequiredSourceArchiveBytes -lt [int64]$Manifest.size.packageLimitBytes -or
        $allBytes -lt [int64]$Manifest.size.unpackedLimitBytes -or $Manifest.size.status -ne 'blocked') {
        throw 'The reviewed M7 package-size blocker is missing.'
    }

    if ($AllowPendingMetadata) {
        foreach ($value in @($Manifest.closure.sha256, $Manifest.metadata.sbom.sha256, $Manifest.metadata.declaredContentDigestSha256)) {
            if ($value -ne 'pending-generator') { Assert-MIGraphXHash ([string]$value) 'metadata hash' }
        }
    } else {
        Assert-MIGraphXHash ([string]$Manifest.closure.sha256) 'closure hash'
        Assert-MIGraphXHash ([string]$Manifest.metadata.sbom.sha256) 'SBOM hash'
        Assert-MIGraphXHash ([string]$Manifest.metadata.declaredContentDigestSha256) 'declared content digest'
    }
    ConvertTo-MIGraphXRelativePath ([string]$Manifest.closure.path) | Out-Null
    ConvertTo-MIGraphXRelativePath ([string]$Manifest.metadata.sbom.path) | Out-Null
    ConvertTo-MIGraphXRelativePath ([string]$Manifest.metadata.provenance.path) | Out-Null
    foreach ($name in @('format', 'packagePath', 'requiredFamily', 'status')) {
        if (-not $Manifest.metadata.packageMarker.ContainsKey($name)) { throw "metadata.packageMarker.$name is required." }
    }
    if ($Manifest.metadata.packageMarker.format -ne 'MIGraphX runtime closure XML v1' -or
        $Manifest.metadata.packageMarker.packagePath -ne 'runtimes/linux-x64/native/migraphx-runtime-closure.xml' -or
        $Manifest.metadata.packageMarker.requiredFamily -ne 'ROCm-7.2.1-linux-x64') {
        throw 'The Runtime package marker contract drifted.'
    }
    if ($Manifest.technicalStatus -eq 'runtime-deferred') {
        if ($Manifest.metadata.packageMarker.status -ne 'not-generated-runtime-deferred' -or
            $null -ne $Manifest.metadata.stagingDigestSha256 -or
            $null -ne $Manifest.metadata.candidatePackageSha256 -or
            $null -ne $Manifest.metadata.promotionReceiptSha256) {
            throw 'A deferred runtime cannot carry a marker, staging, candidate-package, or promotion-receipt identity.'
        }
    }
    if (@($Manifest.blockers).Count -eq 0) { throw 'A deferred runtime must record precise blockers.' }
    $blockerIds = @($Manifest.blockers | ForEach-Object id)
    if (@($blockerIds | Sort-Object -Unique).Count -ne $blockerIds.Count) { throw 'Runtime blocker IDs must be unique.' }

    if ($RequireCandidate) {
        if ($Manifest.technicalStatus -notin @('runtime-candidate-staged', 'runtime-candidate-executed', 'runtime-executed') -or
            -not $Manifest.candidateStaged -or -not $Manifest.verified -or @($Manifest.blockers).Count -ne 0) {
            throw 'MIGRAPHX1001: Runtime packing is disabled until the exact candidate, closure, licenses, size, consumer, package audit, and package-only evidence are closed.'
        }
        if ($Manifest.publishAuthorized -or $Manifest.releaseAuthorized) {
            throw 'MIGRAPHX1001: Source state cannot grant publication or release authorization.'
        }
    }
}

function Get-MIGraphXDeclaredContentDigest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][hashtable] $Manifest,
        [Parameter(Mandatory = $true)][string] $SbomSha256
    )

    $lines = [Collections.Generic.List[string]]::new()
    foreach ($file in @($Manifest.files | Sort-Object path)) {
        $lines.Add("file`t$($file.path)`t$($file.sha256)`t$($file.size)")
    }
    foreach ($license in @($Manifest.licenses | Sort-Object packagePath)) {
        $lines.Add("license`t$($license.packagePath)`t$($license.sha256)")
    }
    $lines.Add("sbom`t$SbomSha256")
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}

Export-ModuleMember -Function Get-MIGraphXSha256, Assert-MIGraphXHash, ConvertTo-MIGraphXRelativePath, Get-MIGraphXRuntimeManifest, Assert-MIGraphXOfficialUrl, Assert-MIGraphXRuntimeManifest, Get-MIGraphXDeclaredContentDigest
