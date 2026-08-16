[CmdletBinding()]
param(
    [string] $HeaderPath,
    [string] $OfficialElfPath,
    [switch] $AcquireInputs,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m2-binding-subset.json') | ConvertFrom-Json
$model = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m3-normalized-api.json') | ConvertFrom-Json
$baseline = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m3-abi-export-baseline.json') | ConvertFrom-Json
$cache = Join-Path $root '.cache\m1'

if ($AcquireInputs) {
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    if (-not $HeaderPath) { $HeaderPath = Join-Path $cache 'migraphx.h' }
    if (-not (Test-Path -LiteralPath $HeaderPath)) { Invoke-WebRequest -Uri $manifest.source.headerUrl -OutFile $HeaderPath }
    if (-not $OfficialElfPath) {
        $deb = Join-Path $cache 'migraphx_2.15.0.70201-81~24.04_amd64.deb'
        if (-not (Test-Path -LiteralPath $deb)) {
            Invoke-WebRequest -Uri 'https://repo.radeon.com/rocm/apt/7.2.1/pool/main/m/migraphx/migraphx_2.15.0.70201-81~24.04_amd64.deb' -OutFile $deb
        }
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $deb).Hash.ToLowerInvariant() -ne $baseline.officialRuntimePackageSha256) {
            throw 'Official runtime package SHA-256 mismatch.'
        }
        $OfficialElfPath = Join-Path $cache 'opt\rocm-7.2.1\lib\libmigraphx_c.so.3.0.70201'
        if (-not (Test-Path -LiteralPath $OfficialElfPath)) {
            Expand-DebDataFile -DebPath $deb -Destination $cache -ArchiveMember './opt/rocm-7.2.1/lib/libmigraphx_c.so.3.0.70201'
        }
    }
}

if (-not $HeaderPath -or -not $OfficialElfPath) { throw 'Specify -HeaderPath and -OfficialElfPath, or use -AcquireInputs.' }
$HeaderPath = (Resolve-Path -LiteralPath $HeaderPath).Path
$OfficialElfPath = (Resolve-Path -LiteralPath $OfficialElfPath).Path
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $HeaderPath).Hash.ToLowerInvariant() -ne $manifest.source.headerSha256) { throw 'Frozen header SHA-256 mismatch.' }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $OfficialElfPath).Hash.ToLowerInvariant() -ne $baseline.officialElfSha256) { throw 'Official ELF SHA-256 mismatch.' }

& (Join-Path $root 'eng\generate-interop.ps1') -HeaderPath $HeaderPath -Verify
if ($LASTEXITCODE -ne 0) { throw 'M3 generation verification failed.' }
$coverageResult = & (Join-Path $root 'eng\verify-m3-coverage.ps1')

$expected = @($model.functions.cName | Sort-Object -Unique)
$unsupported = @($model.functions | Where-Object classification -eq 'unsupported' | ForEach-Object cName | Sort-Object -Unique)
$managed = @($model.functions | Where-Object classification -ne 'unsupported' | ForEach-Object cName | Sort-Object -Unique)
$officialAll = @(& dotnet run --project (Join-Path $root 'tools\ElfExportReader\ElfExportReader.csproj') -c Release -- $OfficialElfPath)
if ($LASTEXITCODE -ne 0) { throw 'Official ELF export reader failed.' }
$classifiedSymbols = @(& dotnet run --project (Join-Path $root 'tools\ElfExportReader\ElfExportReader.csproj') -c Release -- --classify $OfficialElfPath)
if ($LASTEXITCODE -ne 0) { throw 'Official ELF symbol classifier failed.' }
$nonFunctionSymbols = @($classifiedSymbols | Where-Object { -not $_.StartsWith('function|', [StringComparison]::Ordinal) })
$officialPublic = @($officialAll | Where-Object { $_ -in $expected } | Sort-Object -Unique)
$officialExtra = @($officialAll | Where-Object { $_ -notin $expected } | Sort-Object -Unique)
if ((Compare-Object $expected $officialPublic)) { throw 'Official ELF public exports differ from the frozen M3 header inventory.' }
if (Compare-Object @($baseline.exports.officialPrivateFunctions) $officialExtra) { throw 'Official ELF private function classification drifted.' }
if (Compare-Object @($baseline.exports.officialNonFunctionSymbols) $nonFunctionSymbols) { throw 'Official ELF non-function symbol classification drifted.' }
if ($officialAll.Count -ne $baseline.exports.officialMigraphxSymbolCount) { throw 'Official ELF migraphx-prefixed symbol count drifted.' }
if ($expected.Count -ne ($managed.Count + $unsupported.Count)) { throw 'Managed plus unsupported functions do not close over the header.' }

$probeDirectory = Join-Path $root "artifacts\m3-abi\$Configuration"
New-Item -ItemType Directory -Force -Path $probeDirectory | Out-Null
$probeSource = Join-Path $root 'native\m3-abi-probe\probe.c'
$headerDirectory = Split-Path -Parent $HeaderPath
$exportInclude = Join-Path $root 'eng\m3-abi-include'
$probePath = if ($IsWindows -or $env:OS -eq 'Windows_NT') { Join-Path $probeDirectory 'm3-abi-probe.exe' } else { Join-Path $probeDirectory 'm3-abi-probe' }

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    if (-not $installation) { throw 'Visual Studio C++ tools were not found for the M3 ABI probe.' }
    $vcvars = Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'
    $originalEnvironment = @{}
    Get-ChildItem Env: | ForEach-Object { $originalEnvironment[$_.Name] = $_.Value }
    $environmentLines = & $env:ComSpec /d /s /c "`"$vcvars`" >nul && set"
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize the Visual Studio C++ environment.' }
    foreach ($line in $environmentLines) {
        $separator = $line.IndexOf('=')
        if ($separator -gt 0) { [Environment]::SetEnvironmentVariable($line.Substring(0, $separator), $line.Substring($separator + 1), 'Process') }
    }
    try {
        $compiler = (Get-Command cl.exe -ErrorAction Stop).Source
        & $compiler /nologo /TC /W4 /WX /O2 "/I$headerDirectory" "/I$exportInclude" $probeSource "/Fe:$probePath"
        if ($LASTEXITCODE -ne 0) { throw 'Failed to compile the M3 ABI probe.' }
    }
    finally {
        Get-ChildItem Env: | Where-Object { -not $originalEnvironment.ContainsKey($_.Name) } | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null, 'Process') }
        foreach ($entry in $originalEnvironment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
    }
}
else {
    $compiler = Get-Command cc -ErrorAction SilentlyContinue
    if (-not $compiler) { $compiler = Get-Command gcc -ErrorAction SilentlyContinue }
    if (-not $compiler) { throw 'A C compiler is required for the M3 ABI probe.' }
    & $compiler.Source -std=c11 -Wall -Wextra -Werror -O2 -I $headerDirectory -I $exportInclude $probeSource -o $probePath
    if ($LASTEXITCODE -ne 0) { throw 'Failed to compile the M3 ABI probe.' }
}

$probeOutput = @(& $probePath)
if ($LASTEXITCODE -ne 0) { throw 'The M3 ABI probe failed.' }
$probe = @{}
foreach ($line in $probeOutput) {
    $parts = $line -split '=', 2
    if ($parts.Count -ne 2) { throw "Unexpected M3 ABI probe line: $line" }
    $probe[$parts[0]] = [int64]$parts[1]
}
$expectedProbe = @{
    status_size = $baseline.x64Abi.statusSize; shape_datatype_size = $baseline.x64Abi.shapeDatatypeSize;
    size_t_size = $baseline.x64Abi.sizeTSize; bool_size = $baseline.x64Abi.boolSize;
    opaque_handle_size = $baseline.x64Abi.opaqueHandleSize; callback_pointer_size = $baseline.x64Abi.callbackPointerSize;
    status_success = $baseline.x64Abi.statusSuccess; status_bad_param = $baseline.x64Abi.statusBadParam;
    status_unknown_target = $baseline.x64Abi.statusUnknownTarget; status_unknown_error = $baseline.x64Abi.statusUnknownError;
    shape_tuple = $baseline.x64Abi.shapeTuple; shape_fp8e5m2fnuz = $baseline.x64Abi.shapeFp8E5M2Fnuz
}
foreach ($entry in $expectedProbe.GetEnumerator()) {
    if ($probe[$entry.Key] -ne $entry.Value) { throw "M3 ABI probe mismatch for $($entry.Key): expected $($entry.Value), got $($probe[$entry.Key])." }
}

$evidence = [ordered]@{
    '$schema' = 'm3-abi-evidence-v1'
    schemaVersion = '1.0.0'
    evidenceLevel = 'statically-verified'
    sourceHeaderSha256 = $manifest.source.headerSha256
    officialElfSha256 = $baseline.officialElfSha256
    headerFunctions = $expected.Count
    managedEntryPoints = $managed.Count
    unsupportedFunctions = $unsupported
    enums = $model.enums.Count
    handles = $model.handles.Count
    callbacks = $model.callbacks.Count
    officialPublicExports = $officialPublic.Count
    officialAllMigraphxExports = $officialAll.Count
    officialPrivateExtras = $officialExtra
    officialNonFunctionSymbols = $nonFunctionSymbols
    abiProbe = $probe
    runtimeExecuted = $false
    runtimeBoundary = 'Reviewed official execution is bounded to the M1/M2 workflow plus five M9 option setters at 346cdd0b01a7f8039f5deb93058928403fccc7dd; the rest of the M3 inventory remains static ABI evidence.'
}
$evidencePath = Join-Path $probeDirectory 'm3-abi-evidence.json'
[IO.File]::WriteAllText($evidencePath, ($evidence | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))

[PSCustomObject]@{
    HeaderSha256 = $manifest.source.headerSha256
    HeaderFunctions = $expected.Count
    ManagedEntryPoints = $managed.Count
    UnsupportedFunctions = $unsupported.Count
    OfficialPublicExports = $officialPublic.Count
    OfficialAllMIGraphXExports = $officialAll.Count
    Enums = $model.enums.Count
    Handles = $model.handles.Count
    Callbacks = $model.callbacks.Count
    ClassificationClosed = $coverageResult.ClassificationClosed
    EvidencePath = $evidencePath
    Evidence = 'full-header/normalized-model/managed/official-ELF/ABI-probe statically verified; no new runtime execution'
}
