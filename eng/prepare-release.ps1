[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string] $Version,
    [Parameter(Mandatory)][string] $HipSharpPackagePath,
    [Parameter(Mandatory)][string] $HipSharpRepositoryRoot,
    [Parameter(Mandatory)][string] $NativeSourceDirectory
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$releaseNotesPath = Join-Path $root "docs/release-notes/$Version.md"
if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) {
    throw "Release notes are missing: docs/release-notes/$Version.md"
}
$commit = (& git -C $root rev-parse HEAD).Trim()
$origin = (& git -C $root rev-parse origin/main).Trim()
if ((& git -C $root branch --show-current).Trim() -ne 'main' -or $commit -ne $origin -or
    @(& git -C $root status --porcelain).Count -ne 0) {
    throw 'Release preparation requires clean main with HEAD == origin/main.'
}
$HipSharpPackagePath = (Resolve-Path -LiteralPath $HipSharpPackagePath).Path
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $HipSharpPackagePath).Hash.ToLowerInvariant() -ne
    'c05e0e09cab60a5bdab0473b8ce9185548ac5c3289f3fa2eab2c448c00e8faf0') {
    throw 'The reviewed NuGet.org HipSharp 1.0.0 dependency package is required.'
}
Push-Location $root
try {
    $properties = @("-p:DirectoryBuildPropsPath=$(Join-Path $PSScriptRoot 'Workspace.Build.props')",
        "-p:MIGraphXSharpVersion=$Version", "-p:AdapterPackageVersion=$Version",
        "-p:RepositoryCommit=$commit", "-p:HipSharpRepositoryRoot=$HipSharpRepositoryRoot")
    Invoke-DotNet -Arguments (@('restore', './MIGraphXSharp.sln', '--source', 'https://api.nuget.org/v3/index.json') + $properties)
    & "$PSScriptRoot/build.ps1" -Configuration Release -Version $Version -RepositoryCommit $commit -HipSharpRepositoryRoot $HipSharpRepositoryRoot -NoRestore | Out-Host
    & "$PSScriptRoot/test.ps1" -Configuration Release -Version $Version -RepositoryCommit $commit -HipSharpRepositoryRoot $HipSharpRepositoryRoot -NoBuild | Out-Host
    $core = & "$PSScriptRoot/pack.ps1" -Configuration Release -Version $Version -NoBuild | Select-Object -Last 1
    $adapter = & "$PSScriptRoot/pack-adapter.ps1" -Configuration Release -Version $Version -HipSharpPackagePath $HipSharpPackagePath -NoBuild | Select-Object -Last 1
    $index = & "$PSScriptRoot/verify-release-candidate.ps1" -CorePackagePath $core -AdapterPackagePath $adapter -HipSharpPackagePath $HipSharpPackagePath -Version $Version -RepositoryCommit $commit | Select-Object -Last 1
    & "$PSScriptRoot/docs.ps1" -Configuration Release -Version $Version -RepositoryCommit $commit -HipSharpRepositoryRoot $HipSharpRepositoryRoot -NoBuild | Out-Host

    $nativeLock = Get-Content -Raw (Join-Path $root 'eng/contracts/native-runtime-sources.json') | ConvertFrom-Json
    $runtimePackages = @()
    $nativeEvidence = @()
    foreach ($platform in @('win-x64', 'ubuntu.24.04-x64')) {
        $inputLock = $nativeLock.platforms.$platform
        $inputArchive = Join-Path $NativeSourceDirectory $inputLock.archiveAsset
        $runtimePackage = & python3 "$PSScriptRoot/pack-runtime.py" --platform $platform --source-archive $inputArchive --version $Version --commit $commit --output (Join-Path (Get-ArtifactsRoot) 'packages') | Select-Object -Last 1
        if ($LASTEXITCODE -ne 0) { throw "Runtime packaging failed: $platform" }
        & python3 "$PSScriptRoot/pack-runtime.py" --platform $platform --source-archive $inputArchive --version $Version --commit $commit --verify $runtimePackage | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Runtime package audit failed: $platform" }
        $runtimePackages += $runtimePackage
        $nativeEvidence += [ordered]@{
            id = $inputLock.packageId; version = $Version; repositoryCommit = $commit
            package = [IO.Path]::GetFileName($runtimePackage)
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $runtimePackage).Hash.ToLowerInvariant()
            nativeVersion = $inputLock.nativeVersion; rocmVersion = $inputLock.rocmVersion
            sourceArchive = $inputLock.archiveAsset; sourceArchiveSha256 = $inputLock.archiveSha256
            systemDependencies = $inputLock.systemDependencies
        }
    }
    & "$PSScriptRoot/verify-runtime-consumer.ps1" -Version $Version -PackageDirectory (Join-Path (Get-ArtifactsRoot) 'packages') | Out-Host

    $distribution = Join-Path (Get-ArtifactsRoot) "release-dist/$Version/$commit"
    if (Test-Path -LiteralPath $distribution) { throw "Release output already exists: $distribution" }
    New-Item -ItemType Directory -Path $distribution -Force | Out-Null
    Copy-Item -LiteralPath $core, $adapter, $HipSharpPackagePath -Destination $distribution
    Copy-Item -LiteralPath $runtimePackages -Destination $distribution
    $nativeEvidence | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $distribution 'native-package-manifest.json') -Encoding utf8
    $evidence = Get-Content -Raw -LiteralPath $index | ConvertFrom-Json
    Copy-Item -LiteralPath $index -Destination $distribution
    foreach ($item in $evidence.evidence) {
        Copy-Item -LiteralPath (Join-Path (Split-Path $index) $item.path) -Destination $distribution
    }
    $archive = Join-Path $distribution "MIGraphXSharp-v$Version-source.zip"
    & git archive --format=zip "--prefix=MIGraphXSharp-v$Version/" "--output=$archive" $commit
    if ($LASTEXITCODE -ne 0) { throw 'Source archive creation failed.' }
    Compress-Archive -Path (Join-Path (Get-ArtifactsRoot) 'docs/*') -DestinationPath (Join-Path $distribution "MIGraphXSharp-v$Version-docs.zip")
    $notes = (Get-Content -Raw -LiteralPath $releaseNotesPath).
        Replace('](../../', "](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v$Version/").
        Replace('](../', "](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v$Version/docs/")
    $notes += "`nRelease source: ``$commit``. Both MIGraphX packages are built from this exact commit. The adapter is built and consumer-checked against the published HipSharp 1.0.0 dependency. Runtime scope and device-output/vision pipeline behavior are documented in the platform and HIP interop guides; package checks do not extend that scope to other models or devices.`n"
    [IO.File]::WriteAllText((Join-Path $distribution 'RELEASE-NOTES.md'), $notes, [Text.UTF8Encoding]::new($false))
    $checksums = Get-ChildItem -LiteralPath $distribution -File | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $_.Name
    }
    [IO.File]::WriteAllText((Join-Path $distribution 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    Write-Output $distribution
}
finally { Pop-Location }
