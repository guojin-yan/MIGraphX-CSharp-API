Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ReleaseSha256 {
    param([Parameter(Mandatory)][string] $Path)

    $stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = $sha.ComputeHash($stream) } finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
    return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}

function Get-ReleaseBytesSha256 {
    param([Parameter(Mandatory)][byte[]] $Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($Bytes) } finally { $sha.Dispose() }
    return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}

function Get-ReleasePackageIdentity {
    param([Parameter(Mandatory)][string] $Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $archive = [IO.Compression.ZipFile]::OpenRead($resolved)
    try {
        $files = @($archive.Entries |
            Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
            Sort-Object FullName |
            ForEach-Object {
                $stream = $_.Open()
                try {
                    $memory = [IO.MemoryStream]::new()
                    try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $memory.Dispose() }
                }
                finally { $stream.Dispose() }
                [pscustomobject][ordered]@{
                    path = $_.FullName.Replace('\\', '/')
                    size = $bytes.Length
                    sha256 = Get-ReleaseBytesSha256 -Bytes $bytes
                }
            })

        $canonical = ($files | ForEach-Object { "$($_.path)`0$($_.size)`0$($_.sha256)`n" }) -join ''
        $normalized = Get-ReleaseBytesSha256 -Bytes ([Text.Encoding]::UTF8.GetBytes($canonical))

        $nuspecEntry = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($nuspecEntry.Count -ne 1) { throw "Package must contain exactly one nuspec: $resolved" }
        $reader = [IO.StreamReader]::new($nuspecEntry[0].Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.package.metadata
        $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
        $license = if ($licenseNode.type -eq 'expression') { $licenseNode.InnerText } else { 'Apache-2.0 (packaged license file)' }

        return [pscustomobject][ordered]@{
            id = [string] $metadata.id
            version = [string] $metadata.version
            path = $resolved
            size = (Get-Item -LiteralPath $resolved).Length
            sha256 = Get-ReleaseSha256 -Path $resolved
            normalizedContentSha256 = $normalized
            repositoryCommit = [string] $metadata.repository.commit
            license = $license
            files = $files
        }
    }
    finally { $archive.Dispose() }
}

function Write-ReleaseJson {
    param(
        [Parameter(Mandatory)] $Value,
        [Parameter(Mandatory)][string] $Path
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $json = $Value | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText($Path, $json + "`n", [Text.UTF8Encoding]::new($false))
}

function Assert-ReleasePackageIdentity {
    param($Package, [string] $Id, [string] $Version, [string] $RepositoryCommit)

    if ($Package.id -cne $Id -or $Package.version -cne $Version -or $Package.repositoryCommit -cne $RepositoryCommit) {
        throw "Package identity mismatch for $Id (expected $Version at $RepositoryCommit)."
    }
}

function Get-ReleaseDependencyIdentity {
    # This is the distributed package commit, not the adapter's source-build baseline.
    return [pscustomobject]@{
        id = 'JYPPX.ROCm.HIP.CSharp.API'; version = '0.9.1'
        repositoryCommit = '81d124d6a1598680c83c0b398db4d38d181929de'
        sha256 = 'e71398538d7ff5db91c018cac3a2ff57c4d89e71aa77b50942182bd90a2a5fd2'
    }
}

function Assert-ReleaseResolvedPackages {
    param([AllowEmptyCollection()][object[]] $Packages, [string] $Version)

    $expected = @(
        @('JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop', $Version),
        @('JYPPX.ROCm.MIGraphX.CSharp.API', $Version),
        @('JYPPX.ROCm.HIP.CSharp.API', '0.9.1')
    )
    if ($Packages.Count -ne $expected.Count) { throw 'Release consumer must resolve exactly three packages.' }
    foreach ($item in $expected) {
        if (@($Packages | Where-Object { $_.id -ceq $item[0] -and $_.version -ceq $item[1] }).Count -ne 1) {
            throw "Exact consumer dependency is missing: $($item[0]) $($item[1])"
        }
    }
}

function Assert-ReleaseVulnerabilityScan {
    param($Scan, [string] $Version, [string] $RepositoryCommit)

    if ($Scan.schemaVersion -ne '1.0.0' -or $Scan.repositoryCommit -cne $RepositoryCommit -or
        $Scan.projectCount -ne 1 -or $Scan.vulnerablePackageCount -ne 0 -or $Scan.result -cne 'passed' -or
        $Scan.command -cne 'dotnet list adapter-package-consumer package --vulnerable --include-transitive --format json --output-version 1 --no-restore') {
        throw 'Vulnerability scan identity, coverage or result is incorrect.'
    }
    Assert-ReleaseResolvedPackages -Packages @($Scan.resolvedPackages) -Version $Version
}

function Assert-ReleaseNuGetReport {
    param($Report, [switch] $VulnerabilityScan, [string] $ProjectPath)

    if ($Report.version -ne 1 -or @($Report.projects).Count -ne 1) {
        throw 'NuGet report must cover the single adapter consumer.'
    }
    if ($ProjectPath -and -not [IO.Path]::GetFullPath($Report.projects[0].path).Equals(
        [IO.Path]::GetFullPath($ProjectPath), $(if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }))) {
        throw 'NuGet report project differs from the resolved consumer.'
    }
    if ($VulnerabilityScan) {
        # With zero findings NuGet omits frameworks entirely. The separate
        # dependency report proves the framework and exact resolved versions.
        if ($Report.parameters -cne '--vulnerable --include-transitive' -or
            @($Report.sources | Where-Object { $_ -match '^https?://' }).Count -eq 0) {
            throw 'NuGet vulnerability report has no advisory sources or the wrong scan parameters.'
        }
    } elseif (@($Report.projects[0].frameworks).Count -ne 1 -or $Report.projects[0].frameworks[0].framework -cne 'net10.0') {
        throw 'NuGet dependency report must cover the single net10.0 adapter consumer.'
    }
    function Assert-ReportNode($Node) {
        if ($null -eq $Node -or $Node -is [string] -or $Node -is [ValueType]) { return }
        if ($Node -is [Collections.IDictionary]) { $Node = [pscustomobject] $Node }
        if ($Node -is [Collections.IEnumerable] -and $Node -isnot [Management.Automation.PSCustomObject]) {
            foreach ($item in $Node) { Assert-ReportNode $item }
            return
        }
        foreach ($property in $Node.PSObject.Properties) {
            if ($property.Name -in @('problems', 'errors', 'warnings', 'vulnerabilities') -and
                $null -ne $property.Value -and @($property.Value).Count -gt 0) {
                throw "NuGet report contains $($property.Name); release scan is incomplete or failed."
            }
            Assert-ReportNode $property.Value
        }
    }
    Assert-ReportNode $Report
}

Export-ModuleMember -Function Get-ReleaseSha256, Get-ReleasePackageIdentity, Write-ReleaseJson,
    Assert-ReleasePackageIdentity, Get-ReleaseDependencyIdentity, Assert-ReleaseResolvedPackages,
    Assert-ReleaseVulnerabilityScan, Assert-ReleaseNuGetReport
