[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'build-fake-native.ps1') -Configuration $Configuration
}
& (Join-Path $root 'tests/fixtures/generate-onnx-identity.ps1') | Out-Host

$extension = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c.dll' } else { 'libmigraphx_c.so' }
$nativePath = Join-Path (Get-ArtifactsRoot) "fake-native\$Configuration\$extension"
$comparisonMissingName = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'migraphx_c_comparison_missing.dll' } else { 'libmigraphx_c_comparison_missing.so' }
$comparisonMissingPath = Join-Path (Get-ArtifactsRoot) "fake-native\$Configuration\$comparisonMissingName"
$modelPath = Join-Path (Get-ArtifactsRoot) 'models\m2-identity-float32.onnx'
$project = Join-Path $root 'tests\JYPPX.ROCm.MIGraphXSharp.InteropRunner\JYPPX.ROCm.MIGraphXSharp.InteropRunner.csproj'

foreach ($framework in @('net46', 'netcoreapp3.1', 'net7.0', 'net10.0')) {
    if ($framework -eq 'net46' -and -not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
        Write-Output 'net46 fake-native execution is Windows-only; compile coverage remains in the 15-TFM build.'
        continue
    }
    $runArguments = @('run', '--project', $project, '-c', $Configuration, '-f', $framework)
    if ($NoBuild) { $runArguments += '--no-build' }
    $runArguments += @('--', $nativePath, $modelPath)
    & dotnet @runArguments
    if ($LASTEXITCODE -ne 0) { throw "Direct P/Invoke representative execution failed for $framework." }
}

$missingRunArguments = @('run', '--project', $project, '-c', $Configuration, '-f', 'net10.0')
if ($NoBuild) { $missingRunArguments += '--no-build' }
$missingRunArguments += @('--', '--expect-registry-missing', $comparisonMissingPath)
& dotnet @missingRunArguments
if ($LASTEXITCODE -ne 0) { throw 'Registry missing-export diagnostics failed for net10.0.' }

$missingEqualityRunArguments = @('run', '--project', $project, '-c', $Configuration, '-f', 'net10.0')
if ($NoBuild) { $missingEqualityRunArguments += '--no-build' }
$missingEqualityRunArguments += @('--', '--expect-equality-missing', $comparisonMissingPath)
& dotnet @missingEqualityRunArguments
if ($LASTEXITCODE -ne 0) { throw 'Equality missing-export diagnostics failed for net10.0.' }

Write-Output 'Representative Direct P/Invoke graph execution passed for net46, netcoreapp3.1, net7.0, and net10.0; registry and equality missing exports fail closed.'
