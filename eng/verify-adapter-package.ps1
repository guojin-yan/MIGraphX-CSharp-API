[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [string] $Version = '0.0.0',
    [string] $HipSharpVersion = '0.9.1',
    [string] $HipSharpPackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$package = (Resolve-Path -LiteralPath $PackagePath).Path
if ([string]::IsNullOrWhiteSpace($HipSharpPackageDirectory)) {
    $HipSharpPackageDirectory = Join-Path $root '..\..\HIP-CSharp-API\HIP-CSharp-API\artifacts\packages'
}
$HipSharpPackageDirectory = (Resolve-Path -LiteralPath $HipSharpPackageDirectory).Path
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
    foreach ($required in @('README.md','LICENSE')) { if ($required -notin $entries) { throw "Adapter package is missing $required." } }
    $forbidden = @($entries | Where-Object { $_ -match '(?i)(\.cs$|\.pdb$|runtimes/|native/|test|artifact|Radeon_Cloud)' })
    if ($forbidden.Count -ne 0) { throw "Adapter package contains forbidden entries: $($forbidden -join ', ')." }
    $nuspecEntry = @($archive.Entries | Where-Object FullName -like '*.nuspec')
    if ($nuspecEntry.Count -ne 1) { throw 'Adapter package must contain exactly one nuspec.' }
    $reader = [IO.StreamReader]::new($nuspecEntry[0].Open())
    try { $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if (-not $nuspec.Contains("<id>JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop</id>", [StringComparison]::Ordinal) -or
        -not $nuspec.Contains("<version>$Version</version>", [StringComparison]::Ordinal)) { throw 'Adapter package identity is incorrect.' }
    if (@([regex]::Matches($nuspec, "dependency id=`"JYPPX.ROCm.HIP.CSharp.API`" version=`"$([regex]::Escape($HipSharpVersion))`"")).Count -ne 15 -or
        @([regex]::Matches($nuspec, "dependency id=`"JYPPX.ROCm.MIGraphX.CSharp.API`" version=`"$([regex]::Escape($Version))`"")).Count -ne 15) {
        throw 'Adapter dependencies must name exactly the two core packages in every TFM group.'
    }
}
finally { $archive.Dispose() }

$corePackageDirectory = Split-Path -Parent $package
$consumer = Join-Path $root 'tests\fixtures\adapter-package-consumer\Consumer.csproj'
$packages = Join-Path $root "artifacts\adapter-consumer\$([Guid]::NewGuid().ToString('N'))"
& dotnet restore $consumer --source $corePackageDirectory --source $HipSharpPackageDirectory --packages $packages --no-cache --force-evaluate
if ($LASTEXITCODE -ne 0) { throw 'Adapter clean-consumer restore failed.' }
& dotnet build $consumer --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Adapter clean-consumer build failed.' }
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
Write-Output "Adapter package audit passed: $package (sha256 $hash)"
