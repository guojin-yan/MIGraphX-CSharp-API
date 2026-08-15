[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [string] $Version = '0.0.0',
    [string] $HipSharpVersion = '0.9.1',
    [string] $HipSharpPackagePath,
    [string] $HipSharpPackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$package = (Resolve-Path -LiteralPath $PackagePath).Path
if (-not [string]::IsNullOrWhiteSpace($HipSharpPackagePath)) {
    $HipSharpPackagePath = (Resolve-Path -LiteralPath $HipSharpPackagePath).Path
    $HipSharpPackageDirectory = Split-Path -Parent $HipSharpPackagePath
}
elseif ([string]::IsNullOrWhiteSpace($HipSharpPackageDirectory)) {
    $HipSharpPackageDirectory = Join-Path $root '..\..\HIP-CSharp-API\HIP-CSharp-API\artifacts\packages'
}
$HipSharpPackageDirectory = (Resolve-Path -LiteralPath $HipSharpPackageDirectory).Path
if ([string]::IsNullOrWhiteSpace($HipSharpPackagePath)) {
    $HipSharpPackagePath = Join-Path $HipSharpPackageDirectory "JYPPX.ROCm.HIP.CSharp.API.$HipSharpVersion.nupkg"
}
if (-not (Test-Path -LiteralPath $HipSharpPackagePath -PathType Leaf)) { throw "HipSharp package is missing: $HipSharpPackagePath" }
$hipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $HipSharpPackagePath).Hash.ToLowerInvariant()
if ($HipSharpVersion -ne '0.9.1' -or $hipHash -ne 'e71398538d7ff5db91c018cac3a2ff57c4d89e71aa77b50942182bd90a2a5fd2') {
    throw "HipSharp dependency identity mismatch: $HipSharpVersion / $hipHash"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    $frameworks = @('net46','net461','net462','net47','net471','net472','net48','net481','netcoreapp3.1','net5.0','net6.0','net7.0','net8.0','net9.0','net10.0')
    foreach ($framework in $frameworks) {
        foreach ($extension in @('dll','xml')) {
            $expected = "lib/$framework/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.$extension"
            if ($expected -notin $entries) { throw "Adapter package is missing $expected." }
        }
    }
    foreach ($required in @('README.md','LICENSE','NOTICE')) { if ($required -notin $entries) { throw "Adapter package is missing $required." } }
    $forbidden = @($entries | Where-Object { $_ -match '(?i)(\.cs$|\.pdb$|runtimes/|native/|test|artifact|Radeon_Cloud)' })
    if ($forbidden.Count -ne 0) { throw "Adapter package contains forbidden entries: $($forbidden -join ', ')." }
    $nuspecEntry = @($archive.Entries | Where-Object FullName -like '*.nuspec')
    if ($nuspecEntry.Count -ne 1) { throw 'Adapter package must contain exactly one nuspec.' }
    $reader = [IO.StreamReader]::new($nuspecEntry[0].Open())
    try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $nuspec.package.metadata
    if ($metadata.id -ne 'JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop' -or $metadata.version -ne $Version) { throw 'Adapter package identity is incorrect.' }
    if ($metadata.repository.commit -notmatch '^[a-f0-9]{40}$' -or -not $metadata.releaseNotes) { throw 'Adapter repository commit or release notes are missing.' }
    if ($metadata.authors -ne 'Guojin Yan' -or $metadata.copyright -ne 'Copyright 2026 Guojin Yan' -or -not $metadata.readme) { throw 'Adapter authors, copyright, or readme metadata is incorrect.' }
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    if (-not $license -or $license.type -ne 'expression' -or $license.InnerText -ne 'Apache-2.0') { throw 'Adapter license metadata must use Apache-2.0.' }
    $groups = @($metadata.dependencies.group)
    if ($groups.Count -ne 15) { throw "Adapter must contain 15 dependency groups, actual $($groups.Count)." }
    foreach ($group in $groups) {
        $dependencies = @($group.dependency)
        if ($dependencies.Count -ne 2 -or
            @($dependencies | Where-Object { $_.id -eq 'JYPPX.ROCm.HIP.CSharp.API' -and $_.version -eq "[$HipSharpVersion]" }).Count -ne 1 -or
            @($dependencies | Where-Object { $_.id -eq 'JYPPX.ROCm.MIGraphX.CSharp.API' -and $_.version -eq "[$Version]" }).Count -ne 1) {
            throw "Adapter dependency group '$($group.targetFramework)' must use exact closed core ranges."
        }
    }
    foreach ($entry in $archive.Entries | Where-Object { $_.Length -gt 0 }) {
        $stream = $entry.Open()
        try {
            $memory = [IO.MemoryStream]::new()
            try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $memory.Dispose() }
        }
        finally { $stream.Dispose() }
        $utf8 = [Text.Encoding]::UTF8.GetString($bytes)
        $utf16 = [Text.Encoding]::Unicode.GetString($bytes)
        foreach ($marker in @(('E:' + [IO.Path]::DirectorySeparatorChar + 'GitSpace'), ('C:' + [IO.Path]::DirectorySeparatorChar + 'Users'), 'Radeon_Cloud')) {
            if ($utf8.Contains($marker) -or $utf16.Contains($marker)) { throw "Adapter package entry '$($entry.FullName)' contains a machine-specific marker." }
        }
    }
}
finally { $archive.Dispose() }

$corePackageDirectory = Split-Path -Parent $package
$consumer = Join-Path $root 'tests\fixtures\adapter-package-consumer\Consumer.csproj'
$packages = Join-Path $root "artifacts\adapter-consumer\$([Guid]::NewGuid().ToString('N'))"
$feed = Join-Path $root "artifacts\adapter-consumer-feed\$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $feed | Out-Null
Copy-Item -LiteralPath $package -Destination $feed -Force
Copy-Item -LiteralPath (Join-Path $corePackageDirectory "JYPPX.ROCm.MIGraphX.CSharp.API.$Version.nupkg") -Destination $feed -Force
Copy-Item -LiteralPath $HipSharpPackagePath -Destination $feed -Force
$feedUri = ([Uri] $feed).AbsoluteUri
$config = Join-Path $feed 'NuGet.Config'
$configText = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="release-local" value="$feedUri" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="release-local">
      <package pattern="JYPPX.ROCm.MIGraphX.*" />
      <package pattern="JYPPX.ROCm.HIP.CSharp.API" />
    </packageSource>
    <packageSource key="nuget.org"><package pattern="Microsoft.*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@
[IO.File]::WriteAllText($config, $configText, [Text.UTF8Encoding]::new($false))
& dotnet restore $consumer --configfile $config --packages $packages --no-http-cache --force-evaluate -p:MIGraphXConsumerVersion=$Version
if ($LASTEXITCODE -ne 0) { throw 'Adapter clean-consumer restore failed.' }
& dotnet build $consumer --configuration Release --no-restore -p:MIGraphXConsumerVersion=$Version
if ($LASTEXITCODE -ne 0) { throw 'Adapter clean-consumer build failed.' }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
Write-Output "Adapter package audit passed: $package (sha256 $hash)"
