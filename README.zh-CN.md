# MIGraphXSharp

[English / 英文](README.md)

MIGraphXSharp 现已形成未发布的 M11 `0.9.0-rc.8` 本地候选。M11 不新增公开 API；它同步 M10 post-build 官方证据，并新增确定性 M4-M6 fixture、package-only probe、独立复核、冻结的功能/长跑/计时阈值与明确 Windows 策略。仓库默认版本仍为 `0.0.0`，没有发布任何包。

## 状态

- 核心库精确构建从 `net46` 到 `net10.0` 的 15 TFM；.NET 7+ 使用生成的 `LibraryImport`，旧目标使用生成的 `DllImport`。
- 冻结输入为 ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`，header SHA-256 为 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`。
- M1 提供显式/系统原生加载诊断、精确状态映射、严格 UTF-8，以及 target/program 生命周期。
- M2 新增显式文件与字节 buffer ONNX 入口，仅支持一个静态、连续的 float32 输入/输出、同步 GPU target compile、offload-copy、固定输入与复制输出。
- M3 盘点 159 个函数、2 个 enum、25 个 opaque handle 和 6 个 callback。192 个实体按 generated/handwritten-policy/unsupported/configuration-unavailable 闭合为 144/47/1/0；函数分类为 117/41/1/0。
- M4 公开显式 `MIGraphXProgram`、`MIGraphXShape`、`MIGraphXArgument`、`MIGraphXTarget`、ONNX/compile options、parameter map 与复制输出集合。独立的 192 项高层映射闭合为 52 supported、139 planned、1 unsupported。
- M5 新增不可变动态维度、严格的静态/动态 ONNX override、固定版本 `msgpack` Save/Load 和显式根目录的完整性校验模型缓存；映射闭合为 74 supported、117 planned、1 unsupported。
- M6 新增可选 `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` 适配器，包含 3 个公开类型和 11 个成员。它使用固定 C ABI 名称 `ihipStream_t` 提交原生 `migraphx_program_run_async`，并将 program/map/input/output/device 租约保活到 HipStream 完成；映射闭合为 75 supported、116 planned、1 unsupported。
- M7 固定了 ROCm 7.2.1 Ubuntu Noble amd64 的签名源元数据和精确 MIGraphX 根包，并将 `system-native` 冻结为部署模式。用户从 AMD 官方仓库安装完整一致的原生闭包；本项目只分发托管程序集。
- M8 为 core 与 adapter 在全部 15 TFM 上建立版本化兼容基线。`0.x.x` 接口扩展期间可经审查更新该基线，同时候选版本、程序集/file/informational version、缓存 identity、精确包依赖、源码提交、产品 SBOM 与 provenance 继续保持同一证据链。
- M9 为 ONNX Loop 默认值/上限、external-data 根路径、fast-math 与 exhaustive tuning 封装 5 个推理 option 入口；累计映射为 80 supported、111 planned、1 unsupported。在已推送 SHA `346cdd0b01a7f8039f5deb93058928403fccc7dd` 上，ROCm 7.2.1 已接受 5 个记录值，并完成经复核的 gfx1100 Identity 编译/执行与精确 reference 匹配。
- M10 封装 4 个入口：严格 UTF-8 深拷贝的 ONNX parser registry 快照，以及显式 argument/program 原生内容比较。shape equality 继续 planned。随后 `e2386dc69e7640f8ff12d95284e56c3f02c87938` 的 post-build 外部记录独立复核四个 adopted 入口，并在该精确主机/构建上提升为 `runtime-executed`。
- M11 保持 core `27 types / 160 members`、adapter `3 / 11` 与累计 `84/107/1`。rc.5 官方诊断会话通过 registry/lifecycle 检查后，在首轮 file/buffer case 超时，并暴露 TERM 到 KILL 升级缺失；rc.6 新增持久化分阶段轨迹与固定 10 秒强制退出升级，但尚无官方 runtime promotion。隔离负向、长跑与计时仍为 `runtime-deferred`。AMD 将 MIGraphX 2.15.0 文档限定为 Linux 且 Windows 组件表标明 AI libraries 不可用，因此固定版本 Windows runtime 为 `not-applicable`。
- 静态 shape 元数据包含已映射标量类型、lengths、strides、rank、溢出检查后的元素/字节数、standard 与 packed 标志。typed argument 拥有复制后的 host 内存；parameter map 深拷贝 argument；run 输出在原生集合释放前复制。
- 一个 normalized model 同源生成各 158 个 `LibraryImport` 与 `DllImport` EntryPoint。C 可变参数函数 `migraphx_operation_create` 被显式标为 unsupported，不猜测 ABI。
- 固定头中的 159 个函数全部匹配 hash 校验后的官方 ELF；ELF 额外的私有测试导出单独分类。这些 M3 结论属于 `statically-verified`，不是官方 runtime 执行。
- 本地 fake-native 测试实际执行 loader、frontend/export 分类、对象构造、parse、不可变 shape 快照、typed host copy、多项集合、compile、同步 run、定向失败清理及并发/Dispose 边界。它继续作为测试替身证据，与官方 runtime 证据分开记录。
- M1/M2 官方 runtime 已在 `346cdd0b01a7f8039f5deb93058928403fccc7dd` 重新验证：环境为 Ubuntu 24.04 x86-64、ROCm 7.2.1、固定 MIGraphX 包和一张 gfx1100 GPU；实际执行了官方 loader、target/program 生命周期、ONNX file/buffer parse、GPU compile、同步 run 和 Identity reference 对比。
- M6 host 路径要求 `offloadCopy=true`；device input 路径只接受 `HipDeviceMemory`、要求 `offloadCopy=false`，并在 stream 完成后显式 D2H 复制输出。custom op、图编辑/capture 互操作、任意设备指针和 Runtime NuGet 打包仍不在范围内。
- M4/M5/M6 行为只有本地 `statically-verified` 与 `fake-native-executed` 证据；M6 没有官方 GPU、zero-copy、重叠或性能结论。
- core 与 adapter 包均不含 AMD 或 fake-native 二进制。项目不会生成或规划 `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.*` 包；`eng/pack.ps1 -Runtime` 会报告 `MIGRAPHX1001` 并指向 AMD 官方系统仓库。

## 安装

构建仅供本地使用的托管候选包：

```powershell
.\eng\pack.ps1 -Configuration Release -Version 0.9.0-rc.8
```

冻结的 NuGet/项目/程序集名是 `JYPPX.ROCm.MIGraphX.CSharp.API`；C# 命名空间是 `JYPPX.ROCm.MIGraphXSharp`。不得发布此工程候选。

请按照 AMD [ROCm 7.2.1 Linux 官方快速安装指南](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html)安装 ROCm 7.2.1 和 MIGraphX `2.15.0.70201-81~24.04`。在已审计的 Ubuntu 24.04 仓库中，精确 MIGraphX 包名为 `migraphx-rpath7.2.1`；它声明的依赖必须由同一个 AMD 仓库解决，不要复制进应用目录。

## 使用

原生探测必须显式触发。调用者路径必须是绝对路径；loader 不修改 `PATH`、`LD_LIBRARY_PATH` 或 TLS 设置，也不下载原生文件。

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var report = MIGraphXEnvironment.Probe("/opt/rocm-7.2.1/lib/libmigraphx_c.so.3");
Console.WriteLine(report.State); // executed、loaded 或 not-available

var result = MIGraphXOnnxWorkflow.RunFile(
    "/opt/rocm-7.2.1/lib/libmigraphx_c.so.3",
    "/absolute/path/to/identity.onnx",
    new[] { 1f, 2f, 3f, 4f });
Console.WriteLine(string.Join(",", result.Output));

var modelBytes = System.IO.File.ReadAllBytes("/absolute/path/to/identity.onnx");
using var parseOptions = new MIGraphXOnnxOptions("/opt/rocm-7.2.1/lib/libmigraphx_c.so.3");
using var program = MIGraphXProgram.ParseOnnxBuffer(modelBytes, parseOptions);
var inputShape = program.GetParameterShapes()["input"];
```

## M5 动态 Shape 与模型缓存

`MIGraphXDynamicDimension` 与 `MIGraphXShape.CreateDynamic` 表达范围而不暴露 native handle。`MIGraphXOnnxOptions` 接受严格 UTF-8 的静态或动态输入 override；创建 typed host argument 之前仍必须有 concrete 静态 shape。`MIGraphXFileOptions` 将 Save/Load 限定为已测试的 `msgpack` 格式。

`MIGraphXModelCache` 要求调用方显式提供绝对根目录。缓存 key 是规范化模型、固定 header/API、托管构建、native fingerprint、target、compile options、格式和有序 override 的 SHA-256。JSON sidecar（schema 1）校验 payload hash；同目录临时文件通过原子替换写入。`MIGraphXCacheResult` 可观察 hit、miss、损坏和重建来源。缓存不承诺跨 MIGraphX 版本、target、编译参数或 native fingerprint 通用。

`ProbeSystem` 会审计应用 RID、应用基目录和系统 loader 候选。Linux 候选包含 `libmigraphx_c.so.3` 与 `migraphx_c`。对固定 MIGraphX 2.15.0，Windows native provider 按 AMD 官方组件边界为 `not-applicable`，loader 候选只用于诊断；macOS 也没有官方 runtime 支持声明。

## M6 异步 HIP 互操作

可选适配器同时依赖两个托管 core 包，同时保持两个 core 相互独立。`RunHostAsync` 接受以 `offloadCopy=true` 编译的 host parameter map。`RunDeviceAsync` 在借用 `HipDeviceMemory` 前，会校验精确名称、具体 packed shape、容量、runtime client 和 device ordinal，并要求 program 以 `offloadCopy=false` 编译。

`MIGraphXHipAsyncRun.TryComplete` 非阻塞，`Synchronize` 阻塞，`Outputs` 只在 stream 完成并形成 owned host 副本后可用。显式释放 pending 结果会等待完成。适配器不公开裸指针或自由 backend 名称，拒绝 graph capture，并对 device input 的输出执行显式 D2H。状态与所有权契约见 [M6 设计](docs/design/m6-hip-async-interop.md)。

## M7 system-native 部署

经审计的原生闭包对可维护的 Runtime nupkg 过大，而且与 AMD 软件包仓库已经治理的 ROCm 资产重叠。因此 M7 永久拒绝原生 NuGet 分发：managed core 与 adapter 保持无原生载荷，用户通过同一个 AMD 系统仓库和版本族安装 MIGraphX 及 ROCm 依赖。

loader 保留既有显式路径、应用 RID 目录、应用基目录和系统 loader 诊断；它不会下载库或修改 `PATH`/`LD_LIBRARY_PATH`。需要确定性选择时使用绝对路径，不要从 `.cache`、解包后的 Debian 文件或不同 ROCm 版本拼装私有闭包。详见 [M7 设计](docs/design/m7-runtime-packaging.md)、[部署指南](docs/guides/runtime-deployment.md)与 [M7 验证状态](docs/validation/m7-local-validation.md)。

## M8 API 基线与预发布就绪

schema 2 快照记录签名、默认值、泛型约束、nullable metadata、identity 和完全一致的 15 TFM 可用性；`0.x.x` 的有意 API 新增经审查后更新快照。managed SemVer 与 ROCm/MIGraphX 独立；升级 managed 包不会更新 APT。历史 `0.9.0-rc.1`、`0.9.0-rc.2`、`0.9.0-rc.3`、`0.9.0-rc.4` 与 `0.9.0-rc.5` identity 保持不可变；rc.6 adapter 精确恢复 `[0.9.0-rc.6]` core 与 `[0.9.1]` HipSharp。

候选门禁生成逐文件 managed SBOM、本地未签名 provenance、NuGet ZIP hash 与独立的规范化内容 hash。已授权的 `346cdd0...` 会话重新验证了 M1/M2 并执行了 M9 option smoke；M4-M6、system-native 负向、重启/长跑和性能仍未超出既有证据范围。`release-candidate-local` 不等于 `release-ready` 或已发布。

## M9 推理选项与云端记录

`MIGraphXOnnxOptions` 新增非负 Loop 默认值/上限与绝对 strict-UTF-8 external-data 路径；`MIGraphXCompileOptions` 保留既有构造器，并新增显式 fast-math/exhaustive-tune 重载。local 测试验证值转发、路径校验、精确 native 失败定位与清理。无凭据云端脚本已在 clean pushed `346cdd0...` checkout 上执行；回传哈希和独立 JSON 复核把官方 setter 接受与 Identity 编译/执行提升为 `runtime-executed`。Loop 行为、真实 external payload、开启 exhaustive tuning 和代表性 fast-math 精度仍为 planned。

## M10 registry 与原生内容比较

`MIGraphXOnnxWorkflow.GetRegisteredOperators` 返回已加载版本 ONNX parser 名称的只读托管副本；它是 capability hint，不是模型/opset/device 支持保证。`MIGraphXArgument.HasSameNativeContent` 精确比较 host-backed shape/data 内容；`MIGraphXProgram.HasSameNativeContent` 比较固定版本的 program 打印结构。二者都不改变通用 .NET equality/hash/operator 契约。反向并发比较使用稳定的双 owner 锁顺序，并与 Dispose 串行。

本地 fake-native 已覆盖严格 UTF-8、溢出、缺少 export、中途失败、非法 bool、内容差异、反向并发和 Dispose 竞争，并穿过旧/现代两条 interop 路径。后续 M10 post-build 外部记录在精确 rc.2 候选上复核了 registry 稳定性与 argument/program true/false/Dispose case；详见 [M10 设计](docs/design/m10-onnx-registry-native-comparison.md)与 [runtime 计划](docs/validation/m10-runtime-plan.md)。

## M11 官方 Runtime 鲁棒性

M11 生成项目自有、hash 冻结的 Identity、有序 Identity+Neg multi-output 与 dynamic Identity ONNX fixture。`compatibility/m11-runtime-cases.json` 记录每个 M4-M6 正向/拒绝边界、同步/copy 边界、ownership、迭代、超时、前置、证据等级和未覆盖声明。`tools/m11-runtime-probe` 只恢复精确 core/adapter/HipSharp 包，runner 只能写 `runtime-candidate-executed-review-required`，独立 reviewer 再重新计算 identity 与 case 结果。

rc.5 失败的 bounded functional 记录仅为诊断证据；rc.6 尚无官方主机/时间窗执行。fresh-process cache、官方隔离负向、约五小时长跑层和 timing sample 仍未针对新候选执行。enqueue 不是 inference timing，device pointer 不是 zero-copy 结论，也不允许性能比较。详见 [M11 hardening 计划](docs/validation/m11-runtime-hardening-plan.md)。

## 构建

安装 `global.json` 选择的 .NET 10 SDK、PowerShell 7、CMake 和 C 编译器，然后执行：

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\verify-m3-coverage.ps1
.\eng\verify-m4-coverage.ps1
.\eng\verify-m5-coverage.ps1
.\eng\verify-m6-coverage.ps1
.\eng\verify-m9-coverage.ps1
.\eng\verify-m10-coverage.ps1
.\eng\verify-m11-coverage.ps1
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\verify-m2-abi.ps1 -AcquireInputs
.\eng\verify-m3-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.9.0-rc.8 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package -Version 0.9.0-rc.8
$adapter = .\eng\pack-adapter.ps1 -Configuration Release -Version 0.9.0-rc.8 -HipSharpPackagePath $hipPackage -NoBuild
.\eng\verify-adapter-package.ps1 -PackagePath $adapter -Version 0.9.0-rc.8 -HipSharpPackagePath $hipPackage
.\eng\docs.ps1 -Configuration Release -Version 0.9.0-rc.8 -NoBuild
```

构建、官方 ELF 静态证据、fake-native 执行与官方 MIGraphX runtime 执行继续是不同证据层级。M1/M2 runtime 结论只适用于[官方验证摘要](docs/validation/m1-m2-official-runtime.md)记录的精确 SHA、环境、模型、shape 和同步 offload-copy 路径。

## 文档

请阅读 [M8 设计](docs/design/m8-api-release-readiness.md)、[M9 option 设计](docs/design/m9-inference-options.md)、[M10 设计](docs/design/m10-onnx-registry-native-comparison.md)、[M10 本地验证](docs/validation/m10-local-validation.md)、[M11 hardening 计划](docs/validation/m11-runtime-hardening-plan.md)、[API/版本指南](docs/guides/api-versioning.md)、[Runtime 部署指南](docs/guides/runtime-deployment.md)和 [M1/M2 官方验证摘要](docs/validation/m1-m2-official-runtime.md)。

## 许可证

Copyright 2026 Guojin Yan。本托管项目采用 [Apache License 2.0](LICENSE)，归属信息见 [NOTICE](NOTICE)。包中不含 AMD/ROCm/MIGraphX 组件；这些组件继续受各自许可证约束。

## 参与贡献

请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)、[SECURITY.md](SECURITY.md)、[M3 normalized model](compatibility/m3-normalized-api.json) 和 [M10 high-level map](compatibility/m10-high-level-api-map.json)。公开 API 必须提供语义对应的中英文 XML 文档。不得提交 AMD 二进制、fake-native 构建输出、模型、凭据、云端连接信息，或无法由固定输入重现的生成声明。
