[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $Version = '0.0.0',
    [string] $HipSharpVersion = '0.9.1',
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$project = Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop\JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.csproj'
$output = Join-Path $root 'artifacts\packages'
if (-not $NoBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration }
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet pack $project --configuration $Configuration --no-build --output $output `
    -p:MIGraphXSharpVersion=$Version -p:AdapterPackageVersion=$Version -p:HipSharpPackageVersion=$HipSharpVersion | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Adapter pack failed with exit code $LASTEXITCODE." }
$package = Join-Path $output "JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.$Version.nupkg"
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Adapter package was not created: $package" }
Write-Output $package
