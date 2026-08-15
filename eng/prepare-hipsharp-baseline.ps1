[CmdletBinding()]
param(
    [string] $SourceRepository,
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string] $Commit = '81d124d6a1598680c83c0b398db4d38d181929de'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
if ([string]::IsNullOrWhiteSpace($SourceRepository)) {
    $SourceRepository = Join-Path $root '..\..\HIP-CSharp-API\HIP-CSharp-API'
}
$SourceRepository = (Resolve-Path -LiteralPath $SourceRepository).Path
$resolvedCommit = (& git -C $SourceRepository rev-parse "$Commit^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $resolvedCommit -ne $Commit) { throw "HipSharp commit is unavailable: $Commit" }

$parent = Join-Path $root 'artifacts\dependency-source\hipsharp'
$destination = Join-Path $parent $Commit
$marker = Join-Path $destination '.hipsharp-source-commit'
if (Test-Path -LiteralPath $destination) {
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -Raw -LiteralPath $marker).Trim() -ne $Commit) {
        throw "Existing HipSharp baseline directory is incomplete: $destination"
    }
    Write-Output $destination
    return
}

New-Item -ItemType Directory -Force -Path $parent | Out-Null
$archive = Join-Path $parent "$Commit.zip"
& git -C $SourceRepository archive --format=zip "--output=$archive" $Commit
if ($LASTEXITCODE -ne 0) { throw 'Could not archive the exact HipSharp baseline.' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination
[IO.File]::WriteAllText($marker, $Commit + "`n", [Text.UTF8Encoding]::new($false))

$versionText = Get-Content -Raw -LiteralPath (Join-Path $destination 'eng\Versions.props')
if (-not $versionText.Contains('<HipSharpCoreVersion>0.9.1</HipSharpCoreVersion>', [StringComparison]::Ordinal)) {
    throw 'The archived HipSharp baseline is not package version 0.9.1.'
}
Write-Output $destination
