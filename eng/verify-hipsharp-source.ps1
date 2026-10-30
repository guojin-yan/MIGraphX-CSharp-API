[CmdletBinding()]
param([string] $HipSharpRepositoryRoot)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
if ([string]::IsNullOrWhiteSpace($HipSharpRepositoryRoot)) {
    $HipSharpRepositoryRoot = if (-not [string]::IsNullOrWhiteSpace($env:HIPSHARP_REPOSITORY_ROOT)) {
        $env:HIPSHARP_REPOSITORY_ROOT
    } else {
        Join-Path $root '../../HIP-CSharp-API/HIP-CSharp-API'
    }
}
if (-not (Test-Path -LiteralPath $HipSharpRepositoryRoot -PathType Container)) {
    throw 'The HIP adapter requires a HipSharp source checkout. Set HIPSHARP_REPOSITORY_ROOT or pass -HipSharpRepositoryRoot.'
}
$hipRoot = (Resolve-Path -LiteralPath $HipSharpRepositoryRoot).Path
$hipProject = Join-Path $hipRoot 'src/JYPPX.ROCm.HipSharp/JYPPX.ROCm.HipSharp.csproj'
if (-not (Test-Path -LiteralPath $hipProject -PathType Leaf)) { throw "HipSharp source project is missing: $hipProject" }
foreach ($friend in @(
    (Join-Path $root 'src/JYPPX.ROCm.MIGraphX.CSharp.API/Properties/AssemblyInfo.cs'),
    (Join-Path $hipRoot 'src/JYPPX.ROCm.HipSharp/Properties/AssemblyInfo.cs')
)) {
    if (-not (Get-Content -Raw -LiteralPath $friend).Contains('JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop', [StringComparison]::Ordinal)) {
        throw "HIP adapter friend assembly declaration is missing: $friend"
    }
}
Write-Output 'HipSharp source dependency and adapter access contracts passed.'
