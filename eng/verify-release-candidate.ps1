[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $CorePackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $AdapterPackagePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string] $HipSharpPackagePath,
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string] $Version = '0.9.0-rc.1',
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string] $RepositoryCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Import-Module (Join-Path $PSScriptRoot 'release-evidence.psm1') -Force
$root = Get-RepositoryRoot
$head = (& git -C $root rev-parse HEAD).Trim()
$origin = (& git -C $root rev-parse origin/main).Trim()
$branch = (& git -C $root branch --show-current).Trim()
$status = @(& git -C $root status --porcelain)
if ($head -ne $RepositoryCommit -or $origin -ne $RepositoryCommit -or $branch -ne 'main' -or $status.Count -ne 0) {
    throw 'Final release-candidate evidence requires clean main with HEAD == origin/main == RepositoryCommit.'
}

& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $CorePackagePath -Version $Version -RepositoryCommit $RepositoryCommit
& (Join-Path $PSScriptRoot 'verify-adapter-package.ps1') -PackagePath $AdapterPackagePath -Version $Version -RepositoryCommit $RepositoryCommit -HipSharpVersion '0.9.1' -HipSharpPackagePath $HipSharpPackagePath

$consumer = Join-Path $root 'tests\fixtures\adapter-package-consumer\Consumer.csproj'
$resolvedOutput = @(& dotnet list $consumer package --include-transitive --format json --output-version 1 --no-restore 2>&1)
if ($LASTEXITCODE -ne 0) { throw "Could not inspect exact consumer dependencies: $($resolvedOutput -join [Environment]::NewLine)" }
$resolvedText = $resolvedOutput -join "`n"
$start = $resolvedText.IndexOf('{')
$end = $resolvedText.LastIndexOf('}')
if ($start -lt 0 -or $end -lt $start) { throw 'NuGet dependency inspection did not return JSON.' }
$resolved = $resolvedText.Substring($start, $end - $start + 1) | ConvertFrom-Json
Assert-ReleaseNuGetReport -Report $resolved -ProjectPath $consumer
$framework = @($resolved.projects[0].frameworks | Where-Object framework -eq 'net10.0')
$resolvedPackages = @($framework[0].topLevelPackages) + @($framework[0].transitivePackages)
$resolvedIdentities = @($resolvedPackages | ForEach-Object { [pscustomobject]@{ id = $_.id; version = $_.resolvedVersion } })
Assert-ReleaseResolvedPackages -Packages $resolvedIdentities -Version $Version

$scanOutput = @(& dotnet list $consumer package --vulnerable --include-transitive --format json --output-version 1 --no-restore 2>&1)
if ($LASTEXITCODE -ne 0) { throw "NuGet vulnerability scan failed: $($scanOutput -join [Environment]::NewLine)" }
$scanText = $scanOutput -join "`n"
$start = $scanText.IndexOf('{')
$end = $scanText.LastIndexOf('}')
if ($start -lt 0 -or $end -lt $start) { throw 'NuGet vulnerability scan did not return JSON.' }
$scan = $scanText.Substring($start, $end - $start + 1) | ConvertFrom-Json

Assert-ReleaseNuGetReport -Report $scan -VulnerabilityScan -ProjectPath $consumer
$vulnerabilityCount = 0
$evidenceDirectory = Join-Path (Get-ArtifactsRoot) "release-evidence\$Version\$RepositoryCommit"
$scanPath = Join-Path $evidenceDirectory 'vulnerability-scan.json'
$scanSummary = [ordered]@{
    schemaVersion = '1.0.0'
    command = 'dotnet list adapter-package-consumer package --vulnerable --include-transitive --format json --output-version 1 --no-restore'
    repositoryCommit = $RepositoryCommit
    projectCount = @($scan.projects).Count
    vulnerablePackageCount = $vulnerabilityCount
    resolvedPackages = $resolvedIdentities
    result = 'passed'
    sourcePathsOmitted = $true
}
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
[IO.File]::WriteAllText($scanPath, ($scanSummary | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))

$index = & (Join-Path $PSScriptRoot 'new-release-evidence.ps1') `
    -CorePackagePath $CorePackagePath -AdapterPackagePath $AdapterPackagePath -HipSharpPackagePath $HipSharpPackagePath `
    -VulnerabilityScanPath $scanPath -Version $Version -RepositoryCommit $RepositoryCommit -OutputDirectory $evidenceDirectory
& (Join-Path $PSScriptRoot 'verify-release-evidence.ps1') `
    -IndexPath $index -CorePackagePath $CorePackagePath -AdapterPackagePath $AdapterPackagePath `
    -Version $Version -RepositoryCommit $RepositoryCommit
Write-Output $index
