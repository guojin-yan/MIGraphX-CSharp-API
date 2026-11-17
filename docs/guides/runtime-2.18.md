# ROCm 10.1 / MIGraphX 2.18 development

Release 1.0.0 adds optional MIGraphX 2.18 APIs while retaining the frozen 2.15 ABI and all 15 managed target frameworks. It also supplies separate Windows and Ubuntu engine packages; see [deployment](runtime-deployment.md).

1.0.0 新增 MIGraphX 2.18 可选接口和两个平台的独立引擎包，保留 2.15 ABI 和全部 15 个 .NET 目标框架。

## Windows native environment

Validation uses Windows 11 x64, Radeon Pro W7900 (`gfx1100`), ROCm 10.1 and a locally built MIGraphX 2.18. This is a tested local configuration, not a claim that AMD ships this exact Windows build. Its ONNX importer is enabled; its TensorFlow importer is disabled. The original Ubuntu 24.04 / ROCm 7.2.1 validation remains separate.

Install the .NET SDK normally; Visual Studio and `dotnet` discover it without an activation script. The 1.0.0 loader selects Windows target plugins beside the selected engine. Runtime package consumers configure the system HIP dependency paths and HIP/ROCm/Tensile settings as shown in the [deployment guide](runtime-deployment.md). For other native builds, also configure any additional dependencies required by that build. Do not mix DLLs from different installations.

.NET SDK 使用系统标准安装即可被 VS 识别；GPU 原生 DLL 的插件与依赖搜索是另一项运行配置。下面的验证脚本仅配置子进程，既不激活便携式 SDK，也不修改系统 PATH。

For an isolated compatible HipSharp dependency, run `./eng/prepare-development.ps1 -HipSharpSourceRepository <existing-HipSharp-git-checkout>` once. It extracts commit `7b6f75d0047bb87b72094aa910bcea8be2c5e2bc` into the external workspace, preserving uncommitted work in the original checkout. Both Visual Studio and ordinary `dotnet build` then select that snapshot by default. Explicit `HipSharpRepositoryRoot` / `HIPSHARP_REPOSITORY_ROOT` still take precedence. HipSharp compilation and restore files are redirected outside its source through the project reference configuration.

```powershell
./eng/test-runtime.ps1 `
  -MIGraphXRoot D:/AMD/ROCm/10.1.0/MIGraphX `
  -HipRoot D:/AMD/ROCm/10.1.0/HIP `
  -LibTorchRoot D:/AMD/ROCm/10.1.0/LibTorch `
  -HipSharpRepositoryRoot E:/path/to/compatible/HIP-CSharp-API
```

The runner builds through the installed SDK and generates ONNX and TensorFlow fixtures. Build output, models, saved programs and `runtime-validation/current/result.json` go to the external workspace (`MIGRAPHX_WORKSPACE_ROOT`, or the sibling `MIGraphXSharp.Workspace`). `-OutputDirectory` selects another external evidence directory. Each quantization mode runs in a separate child process; the summary includes process exit codes, completed checks and any check interrupted by a native crash. Standard output and native error output are retained for every group.

Each process has a 900-second timeout, configurable with `-GroupTimeoutSeconds`. A timeout terminates that process and its descendants, saves available logs and the last active check, and allows the remaining groups to run. A timed-out process or incomplete JSON report fails the final verification.

Coverage includes file/buffer inference; options and clones; dynamic batches and rejection of invalid sizes; optional symbolic dimensions and trace recovery; save/load and dynamic cache reuse; corruption rebuilds; multiple inputs/outputs; Int64 values; HIP streams, pending guards and early disposal; reference-target custom-op numerics, callback exception containment and normal process exit. FP16, BF16, calibrated INT8 and calibrated FP8 execute a 32×32 matrix product and compare it with a managed reference. Calibration maps are disposed before cloning options, and the original options are disposed before quantization, to exercise snapshot ownership.

The `callbacks` process compiles and launches a HIPRTC increment kernel from a managed GPU custom-op callback, followed by a native ReLU. It checks host synchronous, host asynchronous and device asynchronous execution, output aliasing, disposed wrappers, and compute-exception containment. The kernel uses the borrowed queue returned by the callback context; it never dereferences device data on the CPU. Native 2.15 can bridge its context queue to the caller's stream with events, so the two queue handles need not be identical. Raw callbacks remain responsible for correct native argument and buffer lifetimes.

The `soak` process checks convolution/literal/ReLU numerics, four independent programs with nonblocking streams over 1,000 device runs, and 50 create/compile/early-dispose cycles. Every output element is compared. GPU memory is sampled after warmup with a 64 MiB growth limit; this is a bounded allocation regression, not proof that arbitrary workloads cannot leak. These fixtures target the tested `gfx1100` installations. Set `MIGRAPHX_TEST_GPU_ARCHITECTURE` when compiling the callback kernel for another architecture, then qualify that hardware separately.

For `offloadCopy=false`, allocate every entry returned by the **compiled** program's `GetParameterShapes()`, including output/scratch parameters introduced by the compiler. The device adapter requires the complete parameter map. Keep an uncompiled program or reload the model when comparing different compile settings, rather than repeatedly transforming an already compiled graph.

The same command supports Linux with `-MIGraphXRoot /opt/rocm -HipRoot /opt/rocm`. Missing 2.18 exports on 2.15 are checked for explicit rejection. TensorFlow executes only when its importer exports are present. Device bitcode is located in the native installation's platform-specific directory. On Windows, a single installed hipBLASLt `gfx*` directory is selected automatically; set `HIPBLASLT_TENSILE_LIBPATH` explicitly when selecting between several installed architectures.

验证脚本同时支持 Windows 与 Linux，真实执行量化矩阵乘法、动态缓存、异步生命周期、参考/GPU 自定义算子和多实例循环。结果包含子进程退出码和原生日志；缺失可选接口会验证明确拒绝，不能视为已执行该功能。可用 `-OutputDirectory` 将记录写入指定的源码外目录。GPU 回调组需要该 HIP 安装配套的 HIPRTC；设备参数表必须包含编译器新增的输出/暂存项。

The tested Ubuntu 24.04 / ROCm 7.2.1 / MIGraphX 2.15 / gfx1100 installation fails to link some pointwise and FP8 conversion kernels at its default O3 level (`__ockl_get_*` symbols). The same failure reproduces through the official Python interface. Use the explicit configuration below for this installation; O2 is a native compiler workaround and can affect performance. The script records the selected level and caps compiler parallelism at 12 unless the caller sets `MIGRAPHX_GPU_COMPILE_PARALLEL`.

```powershell
./eng/test-runtime.ps1 -MIGraphXRoot /opt/rocm -HipRoot /opt/rocm -GpuOptimizationLevel 2
```

For applications, set `MIGRAPHX_GPU_OPTIMIZE=2` before starting the process. The managed library does not change native optimization settings. The tested Windows 2.18 configuration uses the native default O3. Very small quantized matrices also need separate qualification: the Windows 2.18 native build crashed on a 2×2 INT8 product; the regression fixture uses 32×32 matrices and does not establish support for that smaller case.

上述 Linux 配置的 O3 原生编译问题在官方 Python 接口同样可复现；可在启动前显式设置 `MIGRAPHX_GPU_OPTIMIZE=2`。这会改变原生优化级别，不应据此比较 O3 性能。Windows 2.18 验证使用默认 O3；2×2 INT8 的原生崩溃仍属于需单独验证的边界，32×32 验证通过不能代表该边界已修复。

## Detect optional capabilities

```csharp
var native = @"D:\AMD\ROCm\10.1.0\MIGraphX\bin\migraphx_c.dll";
bool canTrace = MIGraphXCapabilities.IsSupported(native, MIGraphXFeature.ExecutionTrace);
var missing = MIGraphXCapabilities.GetMissingExports(native, MIGraphXFeature.TensorFlow);
```

Capability checks inspect complete export groups; they do not infer a version or promise model execution. Unsupported optional features throw `NotSupportedException` before calling an absent entry point. The runtime remains fixed to one native library per process. A build without TensorFlow cannot import TensorFlow models; use an ONNX export or a native build with that importer enabled.

功能探测以实际导出为依据。当前本机构建缺少 TensorFlow 导入器，托管层会提前给出清晰错误；托管代码无法补回未编译的原生导入器。

## Compile and target options

```csharp
using var target = new MIGraphXTarget(native, "gpu", "{}");
using var compile = new MIGraphXCompileOptions(
    native, true, false, false, MIGraphXCompileMode.Eager, "{}");
program.Compile(target, compile);
```

Modes are `Eager` (0), `Balanced` (50), and `Max` (100). A null mode/backend JSON keeps native defaults; existing constructors remain usable on 2.15. Target and backend JSON is fully materialized text, validated by the native parser. Percent characters are escaped internally because the upstream functions use printf-style C varargs. Arbitrary format arguments are not exposed. Cloning preserves all native settings and the managed compile metadata.

## ONNX symbols

```csharp
var batch = MIGraphXDynamicDimension.Symbolic("N",
    new Dictionary<string, MIGraphXDynamicDimension> {
        ["N"] = MIGraphXDynamicDimension.Range(1, 8)
    });
using var options = new MIGraphXOnnxOptions(native);
options.SetUseDebugSymbols(true);
options.SetDimensionParameter("batch", batch); // the ONNX dim_param name
options.SetDynamicInputParameterShape("input", new[] {
    batch, MIGraphXDynamicDimension.Fixed(4)
});
```

Symbol dictionaries are immutable copies with numeric bounds. The native parser evaluates expressions. The C API has no getters for evaluated dimension ranges, so symbolic `Minimum`/`Maximum` expose a conservative unknown range; they are not evaluated bounds. Supply an explicit dynamic input override for managed shape validation and use only input sizes accepted by the native expression. Equality, cloning and cache keys include the expression and sorted symbol bounds. Numeric cache identities remain unchanged. When making cache metadata, include all ONNX/target/backend options in its identity and provide the correct native fingerprint/header/API identity for your runtime.

符号表达式与边界由原生库解释，托管层不伪造求值结果；符号维度的最小/最大值仅表示保守未知范围。`SetDimensionParameter` 的名称是 ONNX 的 `dim_param`，不是输入张量名称。

## Execution tracing

```csharp
using var outputs = program.RunTrace(parameters, info =>
    Console.WriteLine($"{info.Index}: {info.Name} ({info.ResultShape})"));
```

`RunTrace` has the same compiled, host-input and `offloadCopy=true` requirements as `Run`. It collects independent metadata during native execution and calls the observer in order after execution and output copying. Observers can retain snapshots. Exceptions from metadata collection are contained inside the native callback and rethrown after it returns; observer exceptions dispose outputs before propagating. `ResultShape` is null for tuples, dynamic shapes or unsupported element types. Intermediate GPU values are not copied or exposed, and this API does not provide live callbacks or timing measurements.

跟踪返回的是可保留的元数据快照；观察器在原生执行结束后调用。不会直接读取 GPU 中间数据，也不提供逐算子实时回调或计时。

## Reproduce the bindings

```powershell
./eng/generate-extensions.ps1 `
  -HeaderPath D:/AMD/ROCm/10.1.0/MIGraphX/include/migraphx/migraphx.h -Verify
```

The extension generator parses the preprocessed header, checks its pinned SHA-256, and emits the 18 reviewed declarations plus their C signatures. It leaves the historical generator and 2.15 contract unchanged. All generated working files stay outside the source tree.
