[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.0.0',
    [string] $RepositoryCommit,
    [switch] $WriteBaseline,
    [switch] $SkipToolBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$RepositoryCommit = if ([string]::IsNullOrWhiteSpace($RepositoryCommit)) { (& git -C $root rev-parse HEAD).Trim() } else { $RepositoryCommit }
if ($RepositoryCommit -notmatch '^[a-f0-9]{40}$') { throw 'Public API verification requires a lowercase 40-character Git SHA.' }

$frameworks = @(
    'net46', 'net461', 'net462', 'net47', 'net471', 'net472', 'net48', 'net481',
    'netcoreapp3.1', 'net5.0', 'net6.0', 'net7.0', 'net8.0', 'net9.0', 'net10.0'
)
$frameworkList = $frameworks -join ';'
$tool = Join-Path $root 'tools\ApiSurface\ApiSurface.csproj'
if (-not $SkipToolBuild) {
    Invoke-DotNet -Arguments @(
        'build', $tool, '-c', 'Release',
        "-p:MIGraphXSharpVersion=$Version",
        "-p:RepositoryCommit=$RepositoryCommit"
    )
}

$surfaces = @(
    @{
        Assembly = 'JYPPX.ROCm.MIGraphX.CSharp.API'
        Package = 'JYPPX.ROCm.MIGraphX.CSharp.API'
        Snapshot = 'compatibility\managed-public-api.txt'
    },
    @{
        Assembly = 'JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop'
        Package = 'JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop'
        Snapshot = 'compatibility\m6-adapter-public-api.txt'
    }
)

foreach ($surface in $surfaces) {
    $written = $false
    foreach ($framework in $frameworks) {
        $assemblyPath = Join-Path $root "artifacts\bin\$($surface.Assembly)\$Configuration\$framework\$($surface.Assembly).dll"
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "Public API assembly is missing: $assemblyPath"
        }
        $mode = if ($WriteBaseline -and -not $written) { '--write' } else { '--check' }
        Invoke-DotNet -Arguments @(
            'run', '--project', $tool, '-c', 'Release', '--no-build', '--',
            '--assembly', $assemblyPath,
            '--snapshot', (Join-Path $root $surface.Snapshot),
            '--package', $surface.Package,
            '--frameworks', $frameworkList,
            '--version', $Version,
            '--commit', $RepositoryCommit,
            $mode
        )
        $written = $true
    }
}

Write-Output "Public API gate passed: core and adapter identities and exact surfaces match across all 15 TFMs for $Version+$RepositoryCommit."
