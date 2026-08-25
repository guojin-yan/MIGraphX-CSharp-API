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
$m9Map = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m9-high-level-api-map.json') | ConvertFrom-Json
$m10Map = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m10-high-level-api-map.json') | ConvertFrom-Json
$cache = Join-Path $root '.cache\m1'

if ($AcquireInputs) {
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    if (-not $HeaderPath) { $HeaderPath = Join-Path $cache 'migraphx.h' }
    if (-not (Test-Path -LiteralPath $HeaderPath)) {
        Invoke-WebRequest -Uri $manifest.source.headerUrl -OutFile $HeaderPath
    }
    if (-not $OfficialElfPath) {
        $deb = Join-Path $cache 'migraphx_2.15.0.70201-81~24.04_amd64.deb'
        if (-not (Test-Path -LiteralPath $deb)) {
            Invoke-WebRequest -Uri 'https://repo.radeon.com/rocm/apt/7.2.1/pool/main/m/migraphx/migraphx_2.15.0.70201-81~24.04_amd64.deb' -OutFile $deb
        }
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $deb).Hash.ToLowerInvariant() -ne 'cf0381824856c7181cfc45db415c1d25a98625090cf06de98de564693c02a01e') {
            throw 'Official runtime package SHA-256 mismatch.'
        }
        Expand-DebDataFile -DebPath $deb -Destination $cache -ArchiveMember './opt/rocm-7.2.1/lib/libmigraphx_c.so.3.0.70201'
        $OfficialElfPath = Join-Path $cache 'opt\rocm-7.2.1\lib\libmigraphx_c.so.3.0.70201'
    }
}

if (-not $HeaderPath -or -not $OfficialElfPath) {
    throw 'Specify -HeaderPath and -OfficialElfPath, or use -AcquireInputs.'
}
$HeaderPath = (Resolve-Path -LiteralPath $HeaderPath).Path
$OfficialElfPath = (Resolve-Path -LiteralPath $OfficialElfPath).Path
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $HeaderPath).Hash.ToLowerInvariant() -ne $manifest.source.headerSha256) {
    throw 'Frozen header SHA-256 mismatch.'
}
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $OfficialElfPath).Hash.ToLowerInvariant() -ne '3b012a738306e2d4499d0aa0dce7b73f96a96209ade45369ad9194c208801aff') {
    throw 'Official ELF SHA-256 mismatch.'
}

& (Join-Path $root 'eng\generate-interop.ps1') -HeaderPath $HeaderPath -Verify
$expected = @($manifest.functions.cName | Sort-Object -Unique)
if ($expected.Count -ne $manifest.scope.subsetFunctionCount) { throw 'M2 subset function count is inconsistent.' }

$generatedText = Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Generated\NativeMethods.LibraryImport.g.cs')
$generatedText += Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Generated\NativeMethods.DllImport.g.cs')
$managed = @([regex]::Matches($generatedText, 'EntryPoint\s*=\s*"(?<name>migraphx_[a-z0-9_]+)"') | ForEach-Object { $_.Groups['name'].Value } | Sort-Object -Unique)
$managedSubset = @($managed | Where-Object { $_ -in $expected })

& (Join-Path $root 'eng\build-fake-native.ps1') -Configuration $Configuration | Out-Host
$fakePath = if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    Join-Path $root "artifacts\fake-native\$Configuration\migraphx_c.dll"
} else {
    Join-Path $root "artifacts\fake-native\$Configuration\libmigraphx_c.so"
}
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    $toolsVersion = Get-ChildItem -LiteralPath (Join-Path $installation 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $dumpbin = Join-Path $toolsVersion.FullName 'bin\Hostx64\x64\dumpbin.exe'
    $fakeDump = & $dumpbin /nologo /exports $fakePath
    $fake = @($fakeDump | ForEach-Object {
        if ($_ -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(?<name>migraphx_[a-z0-9_]+)\s*$') { $Matches.name }
    } | Sort-Object -Unique)
} else {
    $fakeDump = & (Get-Command nm -ErrorAction Stop).Source -D --defined-only $fakePath
    $fake = @([regex]::Matches(($fakeDump -join "`n"), '\bmigraphx_[a-z0-9_]+\b') | ForEach-Object { $_.Value } | Sort-Object -Unique)
}
$fakeSubset = @($fake | Where-Object { $_ -in $expected })
$allowedManagedObjectFakeExports = @(
    'migraphx_shape_create', 'migraphx_shape_destroy', 'migraphx_shape_create_dynamic', 'migraphx_shape_dyn_dims',
    'migraphx_optimals_create', 'migraphx_optimals_destroy',
    'migraphx_dynamic_dimension_create_min_max', 'migraphx_dynamic_dimension_create_min_max_optimals',
    'migraphx_dynamic_dimension_destroy', 'migraphx_dynamic_dimension_is_fixed', 'migraphx_dynamic_dimension_equal',
    'migraphx_dynamic_dimensions_create', 'migraphx_dynamic_dimensions_destroy', 'migraphx_dynamic_dimensions_size', 'migraphx_dynamic_dimensions_get',
    'migraphx_onnx_options_set_input_parameter_shape', 'migraphx_onnx_options_set_dyn_input_parameter_shape',
    'migraphx_onnx_options_set_default_dim_value', 'migraphx_onnx_options_set_default_dyn_dim_value',
    'migraphx_file_options_create', 'migraphx_file_options_set_file_format', 'migraphx_file_options_destroy',
    'migraphx_save', 'migraphx_load',
    'migraphx_program_run_async'
)
$allowedM9FakeExports = @($m9Map.mappings |
    Where-Object { $_.supportStatus -eq 'supported' -and $_.validationLevel -eq 'fake-native-executed' } |
    ForEach-Object { $_.cName } |
    Sort-Object -Unique)
if ($allowedM9FakeExports.Count -ne 5 -or @($allowedM9FakeExports | Where-Object { $_ -notmatch '^migraphx_[a-z0-9_]+$' }).Count -ne 0) {
    throw 'M9 fake-native export review must resolve to exactly five valid C entry points.'
}
$allowedM10FakeExports = @($m10Map.mappings |
    Where-Object { $_.supportStatus -eq 'supported' -and $_.validationLevel -eq 'fake-native-executed' } |
    ForEach-Object { $_.cName } |
    Sort-Object -Unique)
if ($allowedM10FakeExports.Count -ne 4 -or @($allowedM10FakeExports | Where-Object { $_ -notmatch '^migraphx_[a-z0-9_]+$' }).Count -ne 0) {
    throw 'M10 fake-native export review must resolve to exactly four valid adopted C entry points.'
}
$allowedM12FakeExports = @(
    'migraphx_argument_assign_to', 'migraphx_argument_create_empty', 'migraphx_argument_generate',
    'migraphx_argument_load', 'migraphx_argument_save', 'migraphx_arguments_assign_to',
    'migraphx_compile_options_assign_to', 'migraphx_context_finish', 'migraphx_context_get_queue',
    'migraphx_dynamic_dimension_assign_to', 'migraphx_dynamic_dimensions_assign_to',
    'migraphx_experimental_custom_op_assign_to', 'migraphx_experimental_custom_op_create',
    'migraphx_experimental_custom_op_destroy', 'migraphx_experimental_custom_op_register',
    'migraphx_experimental_custom_op_set_compute', 'migraphx_experimental_custom_op_set_compute_shape',
    'migraphx_experimental_custom_op_set_output_alias', 'migraphx_experimental_custom_op_set_runs_on_offload_target',
    'migraphx_file_options_assign_to', 'migraphx_instruction_assign_to', 'migraphx_instruction_destroy',
    'migraphx_instructions_assign_to', 'migraphx_instructions_create', 'migraphx_instructions_destroy',
    'migraphx_module_add_allocation', 'migraphx_module_add_instruction',
    'migraphx_module_add_instruction_with_mod_args', 'migraphx_module_add_literal',
    'migraphx_module_add_parameter', 'migraphx_module_add_return', 'migraphx_module_create',
    'migraphx_module_print', 'migraphx_modules_assign_to', 'migraphx_modules_create',
    'migraphx_modules_destroy', 'migraphx_onnx_options_assign_to', 'migraphx_operation_assign_to',
    'migraphx_operation_create',
    'migraphx_operation_destroy', 'migraphx_operation_name', 'migraphx_optimals_assign_to',
    'migraphx_parse_tf', 'migraphx_parse_tf_buffer', 'migraphx_program_create_module',
    'migraphx_program_experimental_get_context', 'migraphx_program_get_main_module',
    'migraphx_program_parameter_shapes_assign_to', 'migraphx_program_parameters_assign_to',
    'migraphx_program_print', 'migraphx_program_sort', 'migraphx_quantize_bf16',
    'migraphx_quantize_bf16_with_op_names', 'migraphx_quantize_fp16',
    'migraphx_quantize_fp16_with_op_names', 'migraphx_quantize_fp8',
    'migraphx_quantize_fp8_options_add_calibration_data', 'migraphx_quantize_fp8_options_assign_to',
    'migraphx_quantize_fp8_options_create', 'migraphx_quantize_fp8_options_destroy',
    'migraphx_quantize_int8', 'migraphx_quantize_int8_options_add_calibration_data',
    'migraphx_quantize_int8_options_add_op_name', 'migraphx_quantize_int8_options_assign_to',
    'migraphx_quantize_int8_options_create', 'migraphx_quantize_int8_options_destroy',
    'migraphx_quantize_op_names_add', 'migraphx_quantize_op_names_assign_to',
    'migraphx_quantize_op_names_create', 'migraphx_quantize_op_names_destroy',
    'migraphx_shape_assign_to', 'migraphx_shape_create_scalar', 'migraphx_shape_create_with_strides',
    'migraphx_shape_equal', 'migraphx_shape_index', 'migraphx_shape_ndim', 'migraphx_shapes_assign_to',
    'migraphx_tf_options_assign_to', 'migraphx_tf_options_create', 'migraphx_tf_options_destroy',
    'migraphx_tf_options_set_default_dim_value', 'migraphx_tf_options_set_input_parameter_shape',
    'migraphx_tf_options_set_nhwc', 'migraphx_tf_options_set_output_names'
)
if ($allowedM12FakeExports.Count -ne 84 -or @($allowedM12FakeExports | Where-Object { $_ -notmatch '^migraphx_[a-z0-9_]+$' }).Count -ne 0) {
    throw 'M12 fake-native export review must resolve to exactly 84 valid C entry points.'
}
$allowedFakeExports = @($allowedManagedObjectFakeExports + $allowedM9FakeExports + $allowedM10FakeExports + $allowedM12FakeExports)
$unexpectedFakeExports = @($fake | Where-Object { $_ -notin $expected -and $_ -notin $allowedFakeExports })
if ($unexpectedFakeExports.Count -ne 0) {
    throw "fake-native contains exports outside the M2 subset and reviewed M4/M5/M6/M9/M10/M12 test additions: $($unexpectedFakeExports -join ', ')"
}

$officialAll = @(& dotnet run --project (Join-Path $root 'tools\ElfExportReader\ElfExportReader.csproj') -c Release -- $OfficialElfPath)
if ($LASTEXITCODE -ne 0) { throw 'Official ELF export reader failed.' }
$official = @($officialAll | Where-Object { $_ -in $expected } | Sort-Object -Unique)

foreach ($projection in @(
    @{ Name = 'managed M2 EntryPoints'; Values = $managedSubset },
    @{ Name = 'fake-native M2 exports'; Values = $fakeSubset },
    @{ Name = 'official ELF exports'; Values = $official }
)) {
    $difference = Compare-Object $expected $projection.Values
    if ($difference) { throw "$($projection.Name) differ from the M2 subset: $($difference | Out-String)" }
}

$model = & (Join-Path $root 'eng\generate-m2-model.ps1')
$modelPath = $model.ModelPath
if ($model.Sha256 -ne $manifest.reproducibleModel.sha256 -or $model.Bytes -ne $manifest.reproducibleModel.byteLength) {
    throw 'Generated M2 model hash or byte length differs from the frozen manifest.'
}

[PSCustomObject]@{
    HeaderSha256 = $manifest.source.headerSha256
    SubsetFunctions = $expected.Count
    ManagedSubsetEntryPoints = $managedSubset.Count
    ManagedTotalEntryPoints = $managed.Count
    FakeExports = $fakeSubset.Count
    FakeTotalExports = $fake.Count
    OfficialElfExports = $official.Count
    OfficialAllMIGraphXExports = $officialAll.Count
    ModelSha256 = $model.Sha256
    ModelBytes = $model.Bytes
    Evidence = 'header/managed/fake/model and official-ELF statically verified; fake runtime is a test substitute'
}
