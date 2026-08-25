[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.0.0',
    [string] $HipSharpVersion = '0.9.1',
    [string] $HipSharpPackagePath,
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$project = Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop\JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.csproj'
$output = Join-Path $root 'artifacts\packages'
$repositoryCommit = (& git -C $root rev-parse HEAD).Trim()
if ($repositoryCommit -notmatch '^[a-f0-9]{40}$') { throw 'Adapter packaging requires a lowercase 40-character Git SHA.' }
if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $repositoryCommit | Out-Host
}

$corePackage = Join-Path $output "JYPPX.ROCm.MIGraphX.CSharp.API.$Version.nupkg"
if (-not (Test-Path -LiteralPath $corePackage -PathType Leaf)) {
    throw "Pack and verify the core candidate before the adapter: $corePackage"
}
if ([string]::IsNullOrWhiteSpace($HipSharpPackagePath)) {
    $expectedName = "JYPPX.ROCm.HIP.CSharp.API.$HipSharpVersion.nupkg"
    $HipSharpPackagePath = Get-ChildItem -LiteralPath (Join-Path $root 'artifacts') -Filter $expectedName -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash -eq 'E71398538D7FF5DB91C018CAC3A2FF57C4D89E71AA77B50942182BD90A2A5FD2' } |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($HipSharpPackagePath) -or -not (Test-Path -LiteralPath $HipSharpPackagePath -PathType Leaf)) {
    throw 'The exact HipSharp 0.9.1 package is required. Pass -HipSharpPackagePath.'
}
$HipSharpPackagePath = (Resolve-Path -LiteralPath $HipSharpPackagePath).Path
$hipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $HipSharpPackagePath).Hash.ToLowerInvariant()
if ($HipSharpVersion -ne '0.9.1' -or $hipHash -ne 'e71398538d7ff5db91c018cac3a2ff57c4d89e71aa77b50942182bd90a2a5fd2') {
    throw "HipSharp dependency identity mismatch: $HipSharpVersion / $hipHash"
}

$feed = Join-Path $root "artifacts\release-feed\$Version"
$restorePackages = Join-Path $root "artifacts\adapter-pack-restore\$Version"
$restoreIntermediate = Join-Path $root "artifacts\adapter-pack-obj\$Version"
$restoreIntermediateWithSlash = "$restoreIntermediate\"
New-Item -ItemType Directory -Force -Path $feed | Out-Null
Copy-Item -LiteralPath $corePackage -Destination $feed -Force
Copy-Item -LiteralPath $HipSharpPackagePath -Destination $feed -Force
$feedUri = ([Uri] $feed).AbsoluteUri
$restoreConfig = Join-Path $feed 'NuGet.Config'
$restoreConfigText = @"
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
