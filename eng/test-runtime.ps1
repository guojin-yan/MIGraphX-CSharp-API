[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $MIGraphXRoot,
    [Parameter(Mandatory)][string] $HipRoot,
    [string] $LibTorchRoot,
    [string] $HipSharpRepositoryRoot,
    [string] $OutputDirectory,
    [ValidateRange(0, 3)][int] $GpuOptimizationLevel,
    [ValidateRange(1, 86400)][int] $GroupTimeoutSeconds = 900,
    [switch] $NoBuild
)
. (Join-Path $PSScriptRoot 'common.ps1')
Import-Module (Join-Path $PSScriptRoot 'process-runner.psm1') -Force
$root = Get-RepositoryRoot
$workspace = Get-WorkspaceRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $workspace 'runtime-validation/current' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($OutputDirectory.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or $OutputDirectory.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime evidence must be outside the source repository.' }
$MIGraphXRoot = (Resolve-Path -LiteralPath $MIGraphXRoot).Path
$HipRoot = (Resolve-Path -LiteralPath $HipRoot).Path
$windowsRuntime = $IsWindows -or $env:OS -eq 'Windows_NT'
$properties = @()
if ($HipSharpRepositoryRoot) { $properties += "-p:HipSharpRepositoryRoot=$((Resolve-Path -LiteralPath $HipSharpRepositoryRoot).Path)" }
$project = Join-Path $root 'smoke/Runtime218SmokeRunner/Runtime218SmokeRunner.csproj'
if (-not $NoBuild) {
    Invoke-DotNet -Arguments (@('build', $project, '-c', 'Release', '-m:1', "-p:DirectoryBuildPropsPath=$(Join-Path $PSScriptRoot 'Workspace.Build.props')", "-p:RepositoryCommit=$((& git -C $root rev-parse HEAD).Trim())") + $properties)
}
& (Join-Path $root 'tests/fixtures/generate-onnx-identity.ps1') | Out-Host
& (Join-Path $root 'tests/fixtures/generate-tensorflow-quantization.ps1') | Out-Host
$native = Join-Path $MIGraphXRoot $(if ($windowsRuntime) { 'bin/migraphx_c.dll' } else { 'lib/libmigraphx_c.so.3' })
$hip = Join-Path $HipRoot $(if ($windowsRuntime) { 'bin/amdhip64_7.dll' } else { 'lib/libamdhip64.so' })
foreach ($file in @($native, $hip)) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Native library missing: $file" } }
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
if ($PSBoundParameters.ContainsKey('GpuOptimizationLevel')) { $start.Environment['MIGRAPHX_GPU_OPTIMIZE'] = [string]$GpuOptimizationLevel }
if (-not $env:MIGRAPHX_GPU_COMPILE_PARALLEL) { $start.Environment['MIGRAPHX_GPU_COMPILE_PARALLEL'] = [string][Math]::Min(12, [Environment]::ProcessorCount) }
$paths = @((Join-Path $MIGraphXRoot 'bin'), (Join-Path $HipRoot 'bin'), (Join-Path $HipRoot 'lib/llvm/bin'))
if ($LibTorchRoot) { $paths += Join-Path ((Resolve-Path -LiteralPath $LibTorchRoot).Path) 'lib' }
$pathSeparator = [IO.Path]::PathSeparator
$start.Environment['PATH'] = ($paths -join $pathSeparator) + $pathSeparator + $env:PATH
if (-not $windowsRuntime) {
    $libraries = @((Join-Path $MIGraphXRoot 'lib'), (Join-Path $HipRoot 'lib'))
    if ($env:LD_LIBRARY_PATH) { $libraries += $env:LD_LIBRARY_PATH }
    $start.Environment['LD_LIBRARY_PATH'] = $libraries -join ':'
}
$start.Environment['HIP_PATH'] = $HipRoot
$start.Environment['ROCM_PATH'] = $HipRoot
$deviceLibraries = @(
    $(if ($env:HIP_DEVICE_LIB_PATH) { $env:HIP_DEVICE_LIB_PATH }),
    (Join-Path $HipRoot 'amdgcn/bitcode'),
    (Join-Path $HipRoot 'lib/llvm/amdgcn/bitcode')
) | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'ocml.bc') -PathType Leaf) }
if (-not $deviceLibraries) { throw "ROCm device bitcode (ocml.bc) was not found under $HipRoot; set HIP_DEVICE_LIB_PATH explicitly." }
$start.Environment['HIP_DEVICE_LIB_PATH'] = @($deviceLibraries)[0]
$start.Environment['ROCBLAS_TENSILE_LIBPATH'] = Join-Path $HipRoot $(if ($windowsRuntime) { 'bin/rocblas/library' } else { 'lib/rocblas/library' })
# hipBLASLt selects its GPU-specific library beneath this root.
$hipBlasLtLibrary = Join-Path $HipRoot $(if ($windowsRuntime) { 'bin/hipblaslt/library' } else { 'lib/hipblaslt/library' })
# Windows SDK packages put architecture-specific files one directory below this
# root. Let hipBLASLt discover that directory unless the caller selected one.
if ($env:HIPBLASLT_TENSILE_LIBPATH) { $start.Environment['HIPBLASLT_TENSILE_LIBPATH'] = $env:HIPBLASLT_TENSILE_LIBPATH }
elseif (-not $windowsRuntime) { $start.Environment['HIPBLASLT_TENSILE_LIBPATH'] = $hipBlasLtLibrary }
elseif (Test-Path -LiteralPath $hipBlasLtLibrary) {
    $architectures = @(Get-ChildItem -LiteralPath $hipBlasLtLibrary -Directory -Filter 'gfx*')
    if ($architectures.Count -eq 1) { $start.Environment['HIPBLASLT_TENSILE_LIBPATH'] = $architectures[0].FullName }
}
$commonArguments = @(
    (Join-Path (Get-ArtifactsRoot) 'bin/Runtime218SmokeRunner/Release/net10.0/Runtime218SmokeRunner.dll'),
    $native,
    (Join-Path (Get-ArtifactsRoot) 'models/m2-identity-float32.onnx'),
    $hip
)
$runs = @()
foreach ($group in @('general', 'fp16', 'bf16', 'int8', 'fp8', 'callbacks', 'soak')) {
    $directory = Join-Path $OutputDirectory $group
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $reportPath = Join-Path $directory 'result.json'
    if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath }
    $start.ArgumentList.Clear()
    foreach ($argument in ($commonArguments + @($directory, $group))) { $start.ArgumentList.Add($argument) }
    $processResult = Invoke-CapturedProcess -StartInfo $start -OutputDirectory $directory -TimeoutSeconds $GroupTimeoutSeconds
    Get-Content -LiteralPath (Join-Path $directory 'stdout.log') | Out-Host
    Get-Content -LiteralPath (Join-Path $directory 'stderr.log') | Out-Host
    $report = $null
    $reportError = $null
    if (Test-Path -LiteralPath $reportPath) {
        try { $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json }
        catch { $reportError = $_.Exception.Message }
    }
    $runs += [ordered]@{ group = $group; exitCode = $processResult.exitCode; timedOut = $processResult.timedOut; durationMilliseconds = $processResult.durationMilliseconds; reportError = $reportError; report = $report }
}
$failed = @($runs | Where-Object { $_.timedOut -or $_.exitCode -ne 0 -or $_.reportError -or $null -eq $_.report -or $_.report.activeCheck -or $_.report.failures -ne 0 -or @($_.report.checks).Count -eq 0 })
$summary = [ordered]@{
    passed = $failed.Count -eq 0
    groupTimeoutSeconds = $GroupTimeoutSeconds
    environment = [ordered]@{
        gpuOptimizationLevel = $start.Environment['MIGRAPHX_GPU_OPTIMIZE']
        gpuCompileParallelism = $start.Environment['MIGRAPHX_GPU_COMPILE_PARALLEL']
        deviceBitcode = $start.Environment['HIP_DEVICE_LIB_PATH']
        hipBlasLtLibrary = $start.Environment['HIPBLASLT_TENSILE_LIBPATH']
    }
    runs = $runs
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'result.json'), ($summary | ConvertTo-Json -Depth 20))
if ($failed.Count -ne 0) { throw "Native runtime verification failed in $($failed.Count) group(s); see $OutputDirectory/result.json." }
