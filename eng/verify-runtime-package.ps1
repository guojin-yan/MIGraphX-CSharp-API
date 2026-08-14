[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $PackagePath,
    [string] $Manifest = 'nuget/runtime-manifests/linux-x64.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
Import-Module (Join-Path $PSScriptRoot 'runtime-manifest.psm1') -Force
$runtime = (Get-MIGraphXRuntimeManifest (Join-Path $root $Manifest)).Value
Assert-MIGraphXRuntimeManifest $runtime -RequireCandidate
$package = (Resolve-Path -LiteralPath $PackagePath).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $entries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
    $names = @($entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    if (@($names | Group-Object | Where-Object Count -gt 1).Count -ne 0) { throw 'Runtime nupkg contains duplicate paths.' }
    $expected = @($runtime.files | ForEach-Object path) + @($runtime.licenses | ForEach-Object packagePath) + @(
        'runtime-manifest.json', $runtime.metadata.packageMarker.packagePath, [IO.Path]::GetFileName($runtime.metadata.sbom.path),
        [IO.Path]::GetFileName($runtime.metadata.provenance.path), 'README.md', 'LICENSE', 'NOTICE'
    )
    foreach ($path in $expected) { if ($names -notcontains $path) { throw "Runtime nupkg is missing $path." } }
    $unexpected = @($names | Where-Object {
        $_ -notin $expected -and $_ -ne '_rels/.rels' -and $_ -ne '[Content_Types].xml' -and
        $_ -notmatch '^[^/]+\.nuspec$' -and $_ -notmatch '^package/services/metadata/core-properties/[^/]+\.psmdcp$'
    })
    if ($unexpected.Count -ne 0) { throw "Runtime nupkg contains non-allowlisted files: $($unexpected -join ', ')" }
    $forbidden = @($names | Where-Object { $_ -match '(?i)(^|/)(include|cmake|bin|libexec|tests?|models?|cache|artifacts)(/|$)' -or $_ -match '(?i)\.(a|h|hpp|deb|ddeb|pdb|bc|hsaco|onnx|dll)$' })
    if ($forbidden.Count -ne 0) { throw "Runtime nupkg contains forbidden payload: $($forbidden -join ', ')" }
    foreach ($file in @($runtime.files)) {
        $entry = @($entries | Where-Object { $_.FullName.Replace('\', '/') -eq $file.path })[0]
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne $file.sha256 -or $entry.Length -ne [int64]$file.size) { throw "Runtime nupkg file identity mismatch: $($file.path)" }
    }
    $nuspecEntry = @($entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) })
    if ($nuspecEntry.Count -ne 1) { throw 'Runtime nupkg must have exactly one nuspec.' }
    $reader = [IO.StreamReader]::new($nuspecEntry[0].Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($nuspec.package.metadata.id -ne $runtime.packageId -or $nuspec.package.metadata.version -ne $runtime.packageVersion) { throw 'Runtime nuspec ID/version drifted.' }
    $dependencies = @($nuspec.package.metadata.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']/*[local-name()='dependency']"))
    if ($dependencies.Count -ne 1 -or $dependencies[0].id -ne $runtime.topology.runtimeDependency.id -or $dependencies[0].version -ne $runtime.topology.runtimeDependency.version) {
        throw 'Runtime nuspec must contain the exact layered HipSharp runtime dependency.'
    }
} finally { $archive.Dispose() }
if ((Get-Item -LiteralPath $package).Length -ge [int64]$runtime.size.packageLimitBytes) { throw 'Runtime nupkg exceeds the reviewed package-size gate.' }
Write-Host "Runtime package audit passed: $package"
