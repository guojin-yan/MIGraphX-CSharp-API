[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.0.0',
    [string] $RepositoryCommit,
    [string] $HipSharpRepositoryRoot,
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$RepositoryCommit = if ([string]::IsNullOrWhiteSpace($RepositoryCommit)) { (& git -C $root rev-parse HEAD).Trim() } else { $RepositoryCommit }
if ($RepositoryCommit -notmatch '^[a-f0-9]{40}$') { throw 'Documentation requires a lowercase 40-character Git SHA.' }
Push-Location $root
$resolvedHipSharpRepositoryRoot = $HipSharpRepositoryRoot
if ([string]::IsNullOrWhiteSpace($resolvedHipSharpRepositoryRoot) -and -not [string]::IsNullOrWhiteSpace($env:HIPSHARP_REPOSITORY_ROOT)) {
    $resolvedHipSharpRepositoryRoot = $env:HIPSHARP_REPOSITORY_ROOT
}
$previousHipSharpEnvironment = [Environment]::GetEnvironmentVariable('HipSharpRepositoryRoot', 'Process')
if (-not [string]::IsNullOrWhiteSpace($resolvedHipSharpRepositoryRoot)) {
    [Environment]::SetEnvironmentVariable('HipSharpRepositoryRoot', $resolvedHipSharpRepositoryRoot, 'Process')
}
try {
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $RepositoryCommit -HipSharpRepositoryRoot $resolvedHipSharpRepositoryRoot
    }
    Invoke-DotNet -Arguments @('tool', 'restore')
    $workspace = Get-WorkspaceRoot
    $apiDirectory = Join-Path $workspace 'api'
    $siteDirectory = Join-Path (Get-ArtifactsRoot) 'docs'
    $configDirectory = Join-Path (Get-ArtifactsRoot) 'docfx'
    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
    $config = Get-Content -Raw -LiteralPath (Join-Path $root 'docfx.json') | ConvertFrom-Json
    $config.metadata[0].src[0].src = $root
    $config.metadata[0].dest = $apiDirectory
    $config.build.content[0].src = [IO.Path]::GetRelativePath($configDirectory, (Join-Path $root 'docs')).Replace('\', '/')
    $config.build.resource[0].src = $config.build.content[0].src
    $config.build.content[1].src = [IO.Path]::GetRelativePath($configDirectory, $workspace).Replace('\', '/')
    $config.build.dest = $siteDirectory
    $configPath = Join-Path $configDirectory 'docfx.json'
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    $docfxVersion = ((Get-Content -LiteralPath (Join-Path $root '.config\dotnet-tools.json') -Raw | ConvertFrom-Json).tools.docfx.version)
    $nugetPackages = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) { Join-Path $HOME '.nuget\packages' } else { $env:NUGET_PACKAGES }
    $docfxDll = Join-Path $nugetPackages (Join-Path 'docfx' (Join-Path $docfxVersion 'tools/net10.0/any/docfx.dll'))
    if (Test-Path -LiteralPath $docfxDll) {
        Invoke-DotNet -Arguments @($docfxDll, $configPath, '--warningsAsErrors')
    }
    else {
        Invoke-DotNet -Arguments @('tool', 'run', 'docfx', $configPath, '--warningsAsErrors')
    }
    foreach ($relativePath in @(
        'index.html',
        'api/index.html',
        'api/JYPPX.ROCm.MIGraphXSharp.MIGraphXBuildInfo.html'
    )) {
        $path = Join-Path $siteDirectory $relativePath
        if (-not (Test-Path -LiteralPath $path)) { throw "DocFX output is missing $relativePath." }
    }
}
finally {
    [Environment]::SetEnvironmentVariable('HipSharpRepositoryRoot', $previousHipSharpEnvironment, 'Process')
    Pop-Location
}
