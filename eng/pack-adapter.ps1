[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.0.0',
    [string] $HipSharpVersion = '0.10.0',
    [string] $HipSharpPackagePath,
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$project = Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop\JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.csproj'
$output = Join-Path (Get-ArtifactsRoot) 'packages'
$repositoryCommit = (& git -C $root rev-parse HEAD).Trim()
if ($repositoryCommit -notmatch '^[a-f0-9]{40}$') { throw 'Adapter packaging requires a lowercase 40-character Git SHA.' }
if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $repositoryCommit | Out-Host
}

$corePackage = Join-Path $output "JYPPX.ROCm.MIGraphX.CSharp.API.$Version.nupkg"
if (-not (Test-Path -LiteralPath $corePackage -PathType Leaf)) {
    throw "Pack and verify the core candidate before the adapter: $corePackage"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$coreArchive = [IO.Compression.ZipFile]::OpenRead($corePackage)
try {
    $coreNuspecEntries = @($coreArchive.Entries | Where-Object FullName -like '*.nuspec')
    if ($coreNuspecEntries.Count -ne 1) { throw 'Core package must contain exactly one nuspec before adapter packaging.' }
    $coreReader = [IO.StreamReader]::new($coreNuspecEntries[0].Open())
    try { [xml] $coreNuspec = $coreReader.ReadToEnd() } finally { $coreReader.Dispose() }
    $coreMetadata = $coreNuspec.package.metadata
    if ($coreMetadata.id -ne 'JYPPX.ROCm.MIGraphX.CSharp.API' -or $coreMetadata.version -ne $Version) {
        throw 'Core package identity or version does not match the adapter candidate.'
    }
    if ($coreMetadata.repository.commit -ne $repositoryCommit) {
        throw "Core package repository commit mismatch: expected $repositoryCommit, actual $($coreMetadata.repository.commit)."
    }
}
finally { $coreArchive.Dispose() }
if ([string]::IsNullOrWhiteSpace($HipSharpPackagePath)) {
    $expectedName = "JYPPX.ROCm.HIP.CSharp.API.$HipSharpVersion.nupkg"
    $HipSharpPackagePath = Get-ChildItem -LiteralPath (Get-ArtifactsRoot) -Filter $expectedName -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash -eq '096F82AE8D8ECB1CD3AD0E1F5E213E811656CDE10767A0DDF88563747E9A1558' } |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($HipSharpPackagePath) -or -not (Test-Path -LiteralPath $HipSharpPackagePath -PathType Leaf)) {
    throw 'The exact published HipSharp 0.10.0 package is required. Pass -HipSharpPackagePath.'
}
$HipSharpPackagePath = (Resolve-Path -LiteralPath $HipSharpPackagePath).Path
$hipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $HipSharpPackagePath).Hash.ToLowerInvariant()
if ($HipSharpVersion -ne '0.10.0' -or $hipHash -ne '096f82ae8d8ecb1cd3ad0e1f5e213e811656cde10767a0ddf88563747e9a1558') {
    throw "HipSharp dependency identity mismatch: $HipSharpVersion / $hipHash"
}

$feed = Join-Path (Get-ArtifactsRoot) "release-feed\$Version\$repositoryCommit"
$restorePackages = Join-Path (Get-ArtifactsRoot) "adapter-pack-restore\$Version\$repositoryCommit"
$restoreIntermediate = Join-Path (Get-ArtifactsRoot) "adapter-pack-obj\$Version\$repositoryCommit"
$restoreIntermediateWithSlash = "$restoreIntermediate\"
New-Item -ItemType Directory -Force -Path $feed | Out-Null
Copy-Item -LiteralPath $corePackage -Destination $feed -Force
Copy-Item -LiteralPath $HipSharpPackagePath -Destination $feed -Force
$feedSource = [Security.SecurityElement]::Escape($feed)
$restoreConfig = Join-Path $feed 'NuGet.Config'
$restoreConfigText = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="release-local" value="$feedSource" />
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
[IO.File]::WriteAllText($restoreConfig, $restoreConfigText, [Text.UTF8Encoding]::new($false))
& dotnet restore $project --configfile $restoreConfig --packages $restorePackages --no-http-cache --force-evaluate `
    -p:BaseIntermediateOutputPath=$restoreIntermediateWithSlash `
    -p:UseAdapterPackageReferences=true -p:MIGraphXSharpVersion=$Version -p:AdapterPackageVersion=$Version -p:HipSharpPackageVersion=$HipSharpVersion -p:RepositoryCommit=$repositoryCommit | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Adapter package-reference restore failed with exit code $LASTEXITCODE." }
& dotnet build $project --configuration $Configuration --no-restore `
    -p:BaseIntermediateOutputPath=$restoreIntermediateWithSlash `
    -p:UseAdapterPackageReferences=true -p:MIGraphXSharpVersion=$Version -p:AdapterPackageVersion=$Version -p:HipSharpPackageVersion=$HipSharpVersion -p:RepositoryCommit=$repositoryCommit | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Adapter package-reference build failed with exit code $LASTEXITCODE." }
& (Join-Path $PSScriptRoot 'verify-public-api.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $repositoryCommit -SkipToolBuild | Out-Host

New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet pack $project --configuration $Configuration --no-build --output $output `
    -p:BaseIntermediateOutputPath=$restoreIntermediateWithSlash `
    -p:UseAdapterPackageReferences=true -p:MIGraphXSharpVersion=$Version -p:AdapterPackageVersion=$Version -p:HipSharpPackageVersion=$HipSharpVersion -p:RepositoryCommit=$repositoryCommit | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Adapter pack failed with exit code $LASTEXITCODE." }
$package = Join-Path $output "JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.$Version.nupkg"
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Adapter package was not created: $package" }
Write-Output "Adapter package isolated intermediate path: $restoreIntermediate"
Write-Output $package
