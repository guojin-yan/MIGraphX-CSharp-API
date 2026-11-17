[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '1.0.0',
    [switch] $Runtime,
    [ValidateSet('win-x64', 'ubuntu.24.04-x64')][string] $RuntimePlatform,
    [string] $NativeSourceArchive,
    [string] $PythonPath = 'python3',
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$packageDirectory = Join-Path (Get-ArtifactsRoot) 'packages'

if ($Runtime) {
    if (-not $RuntimePlatform -or -not $NativeSourceArchive) { throw 'Runtime packaging requires -RuntimePlatform and -NativeSourceArchive.' }
    & $PythonPath (Join-Path $PSScriptRoot 'pack-runtime.py') --platform $RuntimePlatform --source-archive $NativeSourceArchive --version $Version --output $packageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Runtime package build failed.' }
    return
}

Push-Location $root
try {
    $repositoryCommit = (& git rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $repositoryCommit -notmatch '^[a-f0-9]{40}$') {
        throw 'Core packaging requires a committed 40-character Git SHA.'
    }
    if (-not $NoBuild) {
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $repositoryCommit | Out-Host
    }
    else {
        & (Join-Path $PSScriptRoot 'verify-public-api.ps1') -Configuration $Configuration -Version $Version -RepositoryCommit $repositoryCommit -SkipToolBuild | Out-Host
    }
    New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
    Invoke-DotNet -Arguments @(
        'pack',
        '.\src\JYPPX.ROCm.MIGraphX.CSharp.API\JYPPX.ROCm.MIGraphX.CSharp.API.csproj',
        '-c', $Configuration,
        '--no-build',
        ("-p:MIGraphXSharpVersion=$Version"),
        ("-p:RepositoryCommit=$repositoryCommit"),
        '-o', $packageDirectory
    )
    $package = Join-Path $packageDirectory "JYPPX.ROCm.MIGraphX.CSharp.API.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $package)) {
        throw "Expected package was not produced: $package"
    }
    Write-Output $package
}
finally {
    Pop-Location
}
