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

function Get-MIGraphXSystemRuntimeSourceLock {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string] $Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $value = Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json -AsHashtable
    return [pscustomobject]@{ Path = $resolved; Value = $value }
}

function Assert-MIGraphXSystemRuntimeSourceLock {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][hashtable] $Lock)

    foreach ($name in @('schemaVersion', 'rid', 'nativeVersion', 'source', 'packages')) {
        if (-not $Lock.ContainsKey($name)) { throw "System runtime source lock is missing '$name'." }
    }
    if ($Lock.schemaVersion -ne 2 -or $Lock.rid -ne 'linux-x64' -or
        $Lock.nativeVersion -ne '2.15.0.70201-81~24.04') {
        throw 'System runtime source lock must remain on the audited ROCm 7.2.1/MIGraphX Ubuntu Noble amd64 baseline.'
    }

    $source = $Lock.source
    $expectedUrls = @{
        repositoryUrl = 'https://repo.radeon.com/rocm/apt/7.2.1'
        inReleaseUrl = 'https://repo.radeon.com/rocm/apt/7.2.1/dists/noble/InRelease'
        packagesIndexUrl = 'https://repo.radeon.com/rocm/apt/7.2.1/dists/noble/main/binary-amd64/Packages.gz'
        signingKeyUrl = 'https://repo.radeon.com/rocm/rocm.gpg.key'
    }
    foreach ($name in @('repositoryUrl', 'inReleaseUrl', 'packagesIndexUrl', 'signingKeyUrl')) {
        if (-not $source.ContainsKey($name)) { throw "System runtime source lock is missing source.$name." }
        Assert-MIGraphXOfficialUrl ([string]$source[$name]) "source.$name"
        if (-not [string]::Equals([string]$source[$name], $expectedUrls[$name], [StringComparison]::Ordinal)) {
            throw "System runtime source lock drifted from the audited source.$name."
        }
    }
    foreach ($name in @('signingKeySha256', 'inReleaseSha256', 'packagesIndexSha256', 'headerSha256')) {
        if (-not $source.ContainsKey($name)) { throw "System runtime source lock is missing source.$name." }
        Assert-MIGraphXHash ([string]$source[$name]) "source.$name"
    }
    if ($source.signingKeyFingerprint -ne 'CA8BB4727A47B4D09B4EE8969386B48A1A693C5C' -or
        $source.architecture -ne 'amd64' -or $source.distribution -ne 'noble' -or
        $source.component -ne 'main') {
        throw 'Only the audited signed Ubuntu Noble amd64 source is accepted.'
    }

    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($package in @($Lock.packages)) {
        foreach ($name in @('name', 'version', 'architecture', 'url', 'size', 'sha256', 'role', 'acquisition')) {
            if (-not $package.ContainsKey($name)) { throw "System source package is missing '$name'." }
        }
        if (-not $names.Add([string]$package.name)) { throw "Duplicate system source package: $($package.name)" }
        if ($package.architecture -ne 'amd64') { throw "Wrong source architecture for $($package.name)." }
        if ($package.role -notin @('migraphx-root', 'incremental-provider', 'provided-by-exact-hip-runtime')) {
            throw "Unknown package role for $($package.name)."
        }
        if ($package.acquisition -notin @('required', 'metadata-only')) {
            throw "Unknown acquisition policy for $($package.name)."
        }
        Assert-MIGraphXOfficialUrl ([string]$package.url) "package $($package.name) URL"
        Assert-MIGraphXHash ([string]$package.sha256) "package $($package.name) hash"
        if ([int64]$package.size -le 0) { throw "System source package $($package.name) must have positive size." }
    }
    $rootPackages = @($Lock.packages | Where-Object role -eq 'migraphx-root')
    if ($rootPackages.Count -ne 1 -or $rootPackages[0].name -ne 'migraphx-rpath7.2.1' -or
        $rootPackages[0].acquisition -ne 'required') {
        throw 'The source lock must contain exactly one required MIGraphX 7.2.1 root package.'
    }
}

Export-ModuleMember -Function Get-MIGraphXSha256, Assert-MIGraphXHash, Assert-MIGraphXOfficialUrl, Get-MIGraphXSystemRuntimeSourceLock, Assert-MIGraphXSystemRuntimeSourceLock
