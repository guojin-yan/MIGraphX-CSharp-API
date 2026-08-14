[CmdletBinding()]
param(
    [string] $Manifest = 'nuget/runtime-manifests/linux-x64.json',
    [string] $CacheDirectory = '.cache/runtime/rocm-7.2.1-noble',
    [string] $StagingDirectory = 'artifacts/runtime-staging/linux-x64',
    [switch] $Offline,
    [switch] $VerifyOnly,
    [string] $GpgPath = 'gpg',
    [string] $GpgvPath = 'gpgv'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
Import-Module (Join-Path $PSScriptRoot 'runtime-manifest.psm1') -Force
$manifestPath = if ([IO.Path]::IsPathRooted($Manifest)) { [IO.Path]::GetFullPath($Manifest) } else { [IO.Path]::GetFullPath((Join-Path $root $Manifest)) }
$runtime = (Get-MIGraphXRuntimeManifest $manifestPath).Value
Assert-MIGraphXRuntimeManifest $runtime

function Resolve-UnderRepository([string] $Value) {
    $path = if ([IO.Path]::IsPathRooted($Value)) { [IO.Path]::GetFullPath($Value) } else { [IO.Path]::GetFullPath((Join-Path $root $Value)) }
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime cache/staging paths must remain under the repository: $path"
    }
    return $path
}

function Download-Verified([string] $Url, [string] $Path, [string] $Sha256, [Nullable[int64]] $Size) {
    Assert-MIGraphXOfficialUrl $Url 'download URL'
    Assert-MIGraphXHash $Sha256 'download hash'
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $item = Get-Item -LiteralPath $Path
        if ((Get-MIGraphXSha256 $Path) -eq $Sha256 -and ($null -eq $Size -or $item.Length -eq [int64]$Size)) { return }
        if ($Offline) { throw "Cached runtime input changed: $Path" }
        [IO.File]::Delete($Path)
    } elseif ($Offline) {
        throw "Offline runtime cache is missing: $Path"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $partial = $Path + '.partial'
    if (Test-Path -LiteralPath $partial -PathType Leaf) { [IO.File]::Delete($partial) }
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    try {
        $response = $client.GetAsync([Uri]$Url, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne 200) { throw "HTTP $([int]$response.StatusCode) for $Url" }
            if ($null -ne $response.Headers.Location) { throw "Redirects are not allowed for runtime source acquisition: $Url" }
            $input = $response.Content.ReadAsStream()
            $output = [IO.File]::Create($partial)
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        } finally { $response.Dispose() }
    } finally { $client.Dispose(); $handler.Dispose() }
    if ((Get-MIGraphXSha256 $partial) -ne $Sha256 -or ($null -ne $Size -and (Get-Item -LiteralPath $partial).Length -ne [int64]$Size)) {
        [IO.File]::Delete($partial)
        throw "Downloaded runtime source hash/size mismatch: $Url"
    }
    [IO.File]::Move($partial, $Path)
}

function Get-Executable([string] $Candidate, [string] $Name) {
    if ([IO.Path]::IsPathRooted($Candidate)) {
        if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) { throw "$Name executable was not found: $Candidate" }
        return (Resolve-Path -LiteralPath $Candidate).Path
    }
    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    if ($IsWindows -and $Candidate -eq $Name) {
        $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        foreach ($gitRoot in $roots) {
            $relative = if ($gitRoot -eq $env:LOCALAPPDATA) { "Programs/Git/usr/bin/$Name.exe" } else { "Git/usr/bin/$Name.exe" }
            $gitExecutable = Join-Path $gitRoot $relative
            if (Test-Path -LiteralPath $gitExecutable -PathType Leaf) { return (Resolve-Path -LiteralPath $gitExecutable).Path }
        }
    }
    return $null
}

function Convert-GpgPath([string] $Path, [string] $Executable) {
    if ($IsWindows -and $Executable.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
        return '/' + $Path.Substring(0, 1).ToLowerInvariant() + '/' + $Path.Substring(3).Replace('\', '/')
    }
    return $Path
}

function Read-PackagesIndex([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionMode]::Decompress)
        try {
            $reader = [IO.StreamReader]::new($gzip, [Text.Encoding]::UTF8, $true)
            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally { $gzip.Dispose() }
    } finally { $stream.Dispose() }
    $result = @{}
    foreach ($paragraph in ($text -split "`r?`n`r?`n")) {
        $fields = @{}
        $last = $null
        foreach ($line in ($paragraph -split "`r?`n")) {
            if ($line -match '^([^ :]+):\s*(.*)$') { $last = $Matches[1]; $fields[$last] = $Matches[2] }
            elseif ($line -match '^\s+(.*)$' -and $null -ne $last) { $fields[$last] += ' ' + $Matches[1] }
        }
        if ($fields.ContainsKey('Package')) { $result[[string]$fields.Package] = $fields }
    }
    return $result
}

function Invoke-SignatureVerification([string] $KeyPath, [string] $KeyringPath, [string] $InReleasePath) {
    $gpg = Get-Executable $GpgPath 'gpg'
    $gpgv = Get-Executable $GpgvPath 'gpgv'
    if ($null -ne $gpg) {
        $keyOutput = @(& $gpg --batch --show-keys --with-colons (Convert-GpgPath $KeyPath $gpg) 2>&1)
        $keyExitCode = $LASTEXITCODE
        $fingerprintLine = @($keyOutput | Where-Object { $_ -like 'fpr:*' } | Select-Object -First 1)
        if ($keyExitCode -ne 0 -or $fingerprintLine.Count -ne 1 -or $fingerprintLine[0].Split(':')[9] -ne $runtime.source.signingKeyFingerprint) {
            throw 'AMD archive key fingerprint verification failed.'
        }
        if (-not (Test-Path -LiteralPath $KeyringPath -PathType Leaf)) {
            & $gpg --batch --yes --dearmor -o (Convert-GpgPath $KeyringPath $gpg) (Convert-GpgPath $KeyPath $gpg)
            if ($LASTEXITCODE -ne 0) { throw 'Failed to create the AMD archive keyring.' }
        }
    }
    if ($null -ne $gpgv) {
        $signature = @(& $gpgv --keyring (Convert-GpgPath $KeyringPath $gpgv) (Convert-GpgPath $InReleasePath $gpgv) 2>&1)
        if ($LASTEXITCODE -ne 0 -or ($signature -join "`n") -notmatch [regex]::Escape($runtime.source.signingKeyFingerprint)) { throw 'AMD InRelease signature verification failed.' }
        return
    }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) { throw 'gpgv or Docker is required to verify the signed AMD InRelease metadata.' }
    if (-not (Test-Path -LiteralPath $KeyringPath -PathType Leaf)) {
        throw "A dearmored archive keyring is required when only Docker gpgv is available: $KeyringPath"
    }
    $relativeKeyring = [IO.Path]::GetRelativePath($root, $KeyringPath).Replace('\', '/')
    $relativeRelease = [IO.Path]::GetRelativePath($root, $InReleasePath).Replace('\', '/')
    $signature = @(& $docker.Source run --rm -v "${root}:/work" -w /work ubuntu:24.04 gpgv --keyring "/work/$relativeKeyring" "/work/$relativeRelease" 2>&1)
    if ($LASTEXITCODE -ne 0 -or ($signature -join "`n") -notmatch [regex]::Escape($runtime.source.signingKeyFingerprint)) { throw 'Docker gpgv rejected the AMD InRelease signature.' }
}

$cache = Resolve-UnderRepository $CacheDirectory
$staging = Resolve-UnderRepository $StagingDirectory
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$key = Join-Path $cache 'rocm.gpg.key'
$keyring = Join-Path $cache 'rocm-archive-keyring.gpg'
$inRelease = Join-Path $cache 'InRelease'
$packagesIndex = Join-Path $cache 'Packages.gz'
Download-Verified $runtime.source.signingKeyUrl $key $runtime.source.signingKeySha256 $null
Download-Verified $runtime.source.inReleaseUrl $inRelease $runtime.source.inReleaseSha256 $null
Download-Verified $runtime.source.packagesIndexUrl $packagesIndex $runtime.source.packagesIndexSha256 $null
Invoke-SignatureVerification $key $keyring $inRelease

$releaseText = Get-Content -Raw -LiteralPath $inRelease
$escapedHash = [regex]::Escape([string]$runtime.source.packagesIndexSha256)
if ($releaseText -notmatch "(?m)^\s*$escapedHash\s+\d+\s+main/binary-amd64/Packages\.gz\s*$") {
    throw 'The signed InRelease metadata does not bind the pinned Packages.gz hash.'
}
$index = Read-PackagesIndex $packagesIndex
$downloads = Join-Path $cache 'downloads'
foreach ($package in @($runtime.packages)) {
    if (-not $index.ContainsKey([string]$package.name)) { throw "Pinned package is missing from the signed index: $($package.name)" }
    $record = $index[[string]$package.name]
    $expectedUrl = $runtime.source.repositoryUrl.TrimEnd('/') + '/' + $record.Filename
    if ($record.Version -ne $package.version -or $record.Architecture -ne $package.architecture -or
        [int64]$record.Size -ne [int64]$package.size -or $record.SHA256 -ne $package.sha256 -or $expectedUrl -ne $package.url) {
        throw "Signed package metadata drifted for $($package.name)."
    }
    if ($package.acquisition -eq 'required') {
        $fileName = [IO.Path]::GetFileName(([Uri]$package.url).AbsolutePath)
        if ($fileName -notmatch '^[A-Za-z0-9.+_~-]+\.deb$') { throw "Unsafe Debian package file name: $fileName" }
        Download-Verified $package.url (Join-Path $downloads $fileName) $package.sha256 ([int64]$package.size)
    }
}

if (-not $VerifyOnly) {
    Assert-MIGraphXRuntimeManifest $runtime -RequireCandidate
    throw "MIGRAPHX1001: Candidate staging is not implemented until the M7 blockers are closed: $staging"
}

Write-Host "Signed AMD source lock verified for $($runtime.packageId); staging remains $($runtime.technicalStatus)."
