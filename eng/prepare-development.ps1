[CmdletBinding()]
param([string] $HipSharpSourceRepository)
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$workspace = Get-WorkspaceRoot
$commit = '7b6f75d0047bb87b72094aa910bcea8be2c5e2bc'
if (-not $HipSharpSourceRepository) { $HipSharpSourceRepository = Join-Path $root '../../HIP-CSharp-API/HIP-CSharp-API' }
$HipSharpSourceRepository = (Resolve-Path -LiteralPath $HipSharpSourceRepository).Path
& git -C $HipSharpSourceRepository cat-file -e "$commit^{commit}"
if ($LASTEXITCODE -ne 0) { throw "The compatible HipSharp commit $commit is missing. Fetch it in the dependency repository first." }
$dependency = Join-Path $workspace 'dependencies/HipSharp-7b6f75d0'
$project = Join-Path $dependency 'src/JYPPX.ROCm.HipSharp/JYPPX.ROCm.HipSharp.csproj'
if (-not (Test-Path -LiteralPath $project)) {
    if (Test-Path -LiteralPath $dependency) { throw "Incomplete dependency directory exists; inspect it before retrying: $dependency" }
    $archive = Join-Path $workspace 'downloads/HipSharp-7b6f75d0.tar'
    New-Item -ItemType Directory -Force -Path (Split-Path $archive -Parent) | Out-Null
    & git -C $HipSharpSourceRepository archive --format=tar "--output=$archive" $commit
    if ($LASTEXITCODE -ne 0) { throw 'Unable to archive the fixed dependency.' }
    New-Item -ItemType Directory -Path $dependency | Out-Null
    & tar -xf $archive -C $dependency
    if ($LASTEXITCODE -ne 0) { throw 'Unable to extract the fixed dependency.' }
}
$lockRoot = Join-Path (Get-ArtifactsRoot) 'dependencies/hipsharp'
$sourceLock = Join-Path $dependency 'src/JYPPX.ROCm.HipSharp/packages.lock.json'
$targetLock = Join-Path $lockRoot 'packages.lock.json'
if ((Test-Path -LiteralPath $sourceLock) -and -not (Test-Path -LiteralPath $targetLock)) {
    New-Item -ItemType Directory -Force -Path $lockRoot | Out-Null
    Copy-Item -LiteralPath $sourceLock -Destination $targetLock
}
Write-Output "Compatible HipSharp source: $dependency"
Write-Output 'Visual Studio and dotnet use the installed SDK and this dependency automatically. No environment activation is needed.'
