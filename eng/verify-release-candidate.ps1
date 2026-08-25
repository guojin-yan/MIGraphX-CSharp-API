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
$resolved = $resolvedText.Substring($resolvedText.IndexOf('{'), $resolvedText.LastIndexOf('}') - $resolvedText.IndexOf('{') + 1) | ConvertFrom-Json
$framework = @($resolved.projects[0].frameworks | Where-Object framework -eq 'net10.0')
$resolvedPackages = @($framework[0].topLevelPackages) + @($framework[0].transitivePackages)
foreach ($required in @(
    @('JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop', $Version),
    @('JYPPX.ROCm.MIGraphX.CSharp.API', $Version),
    @('JYPPX.ROCm.HIP.CSharp.API', '0.9.1')
)) {
    if (@($resolvedPackages | Where-Object { $_.id -eq $required[0] -and $_.resolvedVersion -eq $required[1] }).Count -ne 1) {
        throw "Exact consumer dependency is missing: $($required[0]) $($required[1])"
    }
}

$scanOutput = @(& dotnet list $consumer package --vulnerable --include-transitive --format json --output-version 1 --no-restore 2>&1)
if ($LASTEXITCODE -ne 0) { throw "NuGet vulnerability scan failed: $($scanOutput -join [Environment]::NewLine)" }
$scanText = $scanOutput -join "`n"
$start = $scanText.IndexOf('{')
$end = $scanText.LastIndexOf('}')
if ($start -lt 0 -or $end -lt $start) { throw 'NuGet vulnerability scan did not return JSON.' }
$scan = $scanText.Substring($start, $end - $start + 1) | ConvertFrom-Json

function Get-VulnerabilityCount {
    param($Value)
    if ($null -eq $Value) { return 0 }
    if ($Value -is [string]) { return 0 }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [Management.Automation.PSCustomObject]) {
        $count = 0
        foreach ($item in $Value) { $count += Get-VulnerabilityCount -Value $item }
        return $count
    }
    $total = 0
    foreach ($property in $Value.PSObject.Properties) {
        if ($property.Name -eq 'vulnerabilities') { $total += @($property.Value).Count }
        else { $total += Get-VulnerabilityCount -Value $property.Value }
    }
    return $total
}

$vulnerabilityCount = Get-VulnerabilityCount -Value $scan
if ($vulnerabilityCount -ne 0) { throw "NuGet vulnerability scan found $vulnerabilityCount vulnerable package record(s)." }
$evidenceDirectory = Join-Path $root "artifacts\release-evidence\$Version\$RepositoryCommit"
$scanPath = Join-Path $evidenceDirectory 'm8-vulnerability-scan.json'
$scanSummary = [ordered]@{
    schemaVersion = '1.0.0'
    command = 'dotnet list adapter-package-consumer package --vulnerable --include-transitive --format json --output-version 1 --no-restore'
    repositoryCommit = $RepositoryCommit
    projectCount = @($scan.projects).Count
    vulnerablePackageCount = $vulnerabilityCount
    resolvedPackages = @(
        [ordered]@{ id = 'JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop'; version = $Version },
        [ordered]@{ id = 'JYPPX.ROCm.MIGraphX.CSharp.API'; version = $Version },
        [ordered]@{ id = 'JYPPX.ROCm.HIP.CSharp.API'; version = '0.9.1' }
    )
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
