[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string] $Version,
    [Parameter(Mandatory)][string] $HipSharpPackagePath,
    [Parameter(Mandatory)][string] $HipSharpRepositoryRoot
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
    '096f82ae8d8ecb1cd3ad0e1f5e213e811656cde10767a0ddf88563747e9a1558') {
    throw 'The reviewed NuGet.org HipSharp 0.10.0 dependency package is required.'
}
Push-Location $root
try {
    $properties = @("-p:MIGraphXSharpVersion=$Version", "-p:AdapterPackageVersion=$Version",
        "-p:RepositoryCommit=$commit", "-p:HipSharpRepositoryRoot=$HipSharpRepositoryRoot")
    Invoke-DotNet -Arguments (@('restore', './MIGraphXSharp.sln', '--source', 'https://api.nuget.org/v3/index.json') + $properties)
    & "$PSScriptRoot/build.ps1" -Configuration Release -Version $Version -RepositoryCommit $commit -HipSharpRepositoryRoot $HipSharpRepositoryRoot -NoRestore | Out-Host
    Invoke-DotNet -Arguments @('test', './tests/JYPPX.ROCm.MIGraphXSharp.ProjectQuality.Tests/JYPPX.ROCm.MIGraphXSharp.ProjectQuality.Tests.csproj', '-c', 'Release', '--no-build')
    $core = & "$PSScriptRoot/pack.ps1" -Configuration Release -Version $Version -NoBuild | Select-Object -Last 1
    $adapter = & "$PSScriptRoot/pack-adapter.ps1" -Configuration Release -Version $Version -HipSharpPackagePath $HipSharpPackagePath -NoBuild | Select-Object -Last 1
    $index = & "$PSScriptRoot/verify-release-candidate.ps1" -CorePackagePath $core -AdapterPackagePath $adapter -HipSharpPackagePath $HipSharpPackagePath -Version $Version -RepositoryCommit $commit | Select-Object -Last 1
    & "$PSScriptRoot/docs.ps1" -Configuration Release -Version $Version -RepositoryCommit $commit -HipSharpRepositoryRoot $HipSharpRepositoryRoot -NoBuild | Out-Host

    $distribution = Join-Path (Get-ArtifactsRoot) "release-dist/$Version/$commit"
    if (Test-Path -LiteralPath $distribution) { throw "Release output already exists: $distribution" }
    New-Item -ItemType Directory -Path $distribution -Force | Out-Null
    Copy-Item -LiteralPath $core, $adapter, $HipSharpPackagePath -Destination $distribution
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
    $notes += "`nRelease source: ``$commit``. Both MIGraphX packages are built from this exact commit. The adapter is built and consumer-checked against the published HipSharp 0.10.0 dependency. Runtime scope and device-output/vision pipeline behavior are documented in the platform and HIP interop guides; package checks do not extend that scope to other models or devices.`n"
    [IO.File]::WriteAllText((Join-Path $distribution 'RELEASE-NOTES.md'), $notes, [Text.UTF8Encoding]::new($false))
    $checksums = Get-ChildItem -LiteralPath $distribution -File | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $_.Name
    }
    [IO.File]::WriteAllText((Join-Path $distribution 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    Write-Output $distribution
}
finally { Pop-Location }
