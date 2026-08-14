# MIGraphXSharp

[English / 英文](README.md)

MIGraphXSharp `0.0.0` 现已包含 AMD 官方 MIGraphX C API 的 M1 生命周期基础、M2 受限 ONNX 闭环、M3 可复现低层绑定生产线、M4 资源安全同步对象层、M5 动态 Shape/缓存策略、可选的 M6 HipSharp 异步适配器，以及 M7 默认关闭的 Runtime 供应链基础设施。当前仍是本地工程候选，不是已发布版本。

## 状态

- 核心库精确构建从 `net46` 到 `net10.0` 的 15 TFM；.NET 7+ 使用生成的 `LibraryImport`，旧目标使用生成的 `DllImport`。
- 冻结输入为 ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`，header SHA-256 为 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`。
- M1 提供显式/系统原生加载诊断、精确状态映射、严格 UTF-8，以及 target/program 生命周期。
- M2 新增显式文件与字节 buffer ONNX 入口，仅支持一个静态、连续的 float32 输入/输出、同步 GPU target compile、offload-copy、固定输入与复制输出。
- M3 盘点 159 个函数、2 个 enum、25 个 opaque handle 和 6 个 callback。192 个实体按 generated/handwritten-policy/unsupported/configuration-unavailable 闭合为 144/47/1/0；函数分类为 117/41/1/0。
- M4 公开显式 `MIGraphXProgram`、`MIGraphXShape`、`MIGraphXArgument`、`MIGraphXTarget`、ONNX/compile options、parameter map 与复制输出集合。独立的 192 项高层映射闭合为 52 supported、139 planned、1 unsupported。
- M5 新增不可变动态维度、严格的静态/动态 ONNX override、固定版本 `msgpack` Save/Load 和显式根目录的完整性校验模型缓存；映射闭合为 74 supported、117 planned、1 unsupported。
- M6 新增可选 `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` 适配器，包含 3 个公开类型和 11 个成员。它使用固定名称 `hipStream_t` 提交原生 `migraphx_program_run_async`，并将 program/map/input/output/device 租约保活到 HipStream 完成；映射闭合为 75 supported、116 planned、1 unsupported。
- M7 固定了 ROCm 7.2.1 Ubuntu Noble amd64 的签名源元数据、精确 MIGraphX 根包、6 个 canonical MIGraphX ELF 与 6 个物化 alias、1 份根包许可证、CycloneDX 1.5 SBOM、provenance 和依赖证据。这些证据是 `statically-verified`；Runtime 仍为 `runtime-deferred`。
- 静态 shape 元数据包含已映射标量类型、lengths、strides、rank、溢出检查后的元素/字节数、standard 与 packed 标志。typed argument 拥有复制后的 host 内存；parameter map 深拷贝 argument；run 输出在原生集合释放前复制。
- 一个 normalized model 同源生成各 158 个 `LibraryImport` 与 `DllImport` EntryPoint。C 可变参数函数 `migraphx_operation_create` 被显式标为 unsupported，不猜测 ABI。
- 固定头中的 159 个函数全部匹配 hash 校验后的官方 ELF；ELF 额外的私有测试导出单独分类。这些 M3 结论属于 `statically-verified`，不是官方 runtime 执行。
- 本地 fake-native 测试实际执行 loader、frontend/export 分类、对象构造、parse、不可变 shape 快照、typed host copy、多项集合、compile、同步 run、定向失败清理及并发/Dispose 边界。它继续作为测试替身证据，与官方 runtime 证据分开记录。
- M1/M2 官方 runtime 已在 `f1a11cfd1701a041cee29188f7600c85b34ae260` 通过：环境为 Ubuntu 24.04 x86-64、ROCm 7.2.1、固定 MIGraphX 包和一张 gfx1100 GPU；实际执行了官方 loader、target/program 生命周期、ONNX file/buffer parse、GPU compile、同步 run 和 Identity reference 对比。
- M6 host 路径要求 `offloadCopy=true`；device input 路径只接受 `HipDeviceMemory`、要求 `offloadCopy=false`，并在 stream 完成后显式 D2H 复制输出。custom op、图编辑/capture 互操作、任意设备指针和可用的 Runtime NuGet 包仍不在范围内。
- M4/M5/M6 行为只有本地 `statically-verified` 与 `fake-native-executed` 证据；M6 没有官方 GPU、zero-copy、重叠或性能结论。
- 核心包不含 AMD 或 fake-native 二进制。`JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64` `7.2.1` 只是被阻塞的包身份和工程骨架；当前没有 Runtime nupkg，受控入口与直接 pack 都以 `MIGRAPHX1001` 失败关闭。

## 安装

构建仅供本地使用的托管候选包：

```powershell
.\eng\pack.ps1 -Configuration Release -Version 0.0.0
```

冻结的 NuGet/项目/程序集名是 `JYPPX.ROCm.MIGraphX.CSharp.API`；C# 命名空间是 `JYPPX.ROCm.MIGraphXSharp`。不得发布此工程候选。

## 使用

原生探测必须显式触发。调用者路径必须是绝对路径；loader 不修改 `PATH`、`LD_LIBRARY_PATH` 或 TLS 设置，也不下载原生文件。

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var report = MIGraphXEnvironment.Probe(@"C:\absolute\path\to\migraphx_c.dll");
Console.WriteLine(report.State); // executed、loaded 或 not-available

var result = MIGraphXOnnxWorkflow.RunFile(
    @"C:\absolute\path\to\migraphx_c.dll",
    @"C:\absolute\path\to\identity.onnx",
    new[] { 1f, 2f, 3f, 4f });
Console.WriteLine(string.Join(",", result.Output));

var modelBytes = System.IO.File.ReadAllBytes(@"C:\absolute\path\to\identity.onnx");
using var parseOptions = new MIGraphXOnnxOptions(@"C:\absolute\path\to\migraphx_c.dll");
using var program = MIGraphXProgram.ParseOnnxBuffer(modelBytes, parseOptions);
var inputShape = program.GetParameterShapes()["input"];
```

## M5 动态 Shape 与模型缓存

`MIGraphXDynamicDimension` 与 `MIGraphXShape.CreateDynamic` 表达范围而不暴露 native handle。`MIGraphXOnnxOptions` 接受严格 UTF-8 的静态或动态输入 override；创建 typed host argument 之前仍必须有 concrete 静态 shape。`MIGraphXFileOptions` 将 Save/Load 限定为已测试的 `msgpack` 格式。

`MIGraphXModelCache` 要求调用方显式提供绝对根目录。缓存 key 是规范化模型、固定 header/API、托管构建、native fingerprint、target、compile options、格式和有序 override 的 SHA-256。JSON sidecar（schema 1）校验 payload hash；同目录临时文件通过原子替换写入。`MIGraphXCacheResult` 可观察 hit、miss、损坏和重建来源。缓存不承诺跨 MIGraphX 版本、target、编译参数或 native fingerprint 通用。

`ProbeSystem` 会审计应用 RID、应用基目录和系统 loader 候选。Linux 候选包含 `libmigraphx_c.so.3` 与 `migraphx_c`。Windows/macOS 只实现诚实诊断候选，不作官方 MIGraphX runtime 支持声明。

## M6 异步 HIP 互操作

可选适配器同时依赖两个托管 core 包，同时保持两个 core 相互独立。`RunHostAsync` 接受以 `offloadCopy=true` 编译的 host parameter map。`RunDeviceAsync` 在借用 `HipDeviceMemory` 前，会校验精确名称、具体 packed shape、容量、runtime client 和 device ordinal，并要求 program 以 `offloadCopy=false` 编译。

`MIGraphXHipAsyncRun.TryComplete` 非阻塞，`Synchronize` 阻塞，`Outputs` 只在 stream 完成并形成 owned host 副本后可用。显式释放 pending 结果会等待完成。适配器不公开裸指针或自由 backend 名称，拒绝 graph capture，并对 device input 的输出执行显式 D2H。状态与所有权契约见 [M6 设计](docs/design/m6-hip-async-interop.md)。

## M7 Runtime 打包状态

M7 原则上选择分层拓扑：未来的 MIGraphX Runtime 精确依赖 `JYPPX.ROCm.HIP.CSharp.API.Runtime.linux-x64` `[7.2.1]`，自身只携带 MIGraphX/provider 增量。独立索引得到的增量来源归档合计 2,195,081,068 字节，仅必需的 hipBLASLt 归档就有 1,613,836,012 字节，超过 262,144,000 字节包门槛。provider payload/许可证清单、package-only RPATH/load trace、跨程序集 family identity、clean Runtime consumer 与新的官方主机执行仍未闭合。

loader 为未来包保留 `runtimes/linux-x64/native/lib`。在该目录发现候选时必须存在 `migraphx-runtime-closure.xml`；native load 前逐项核对文件 hash、SONAME、包/RID/版本与 ROCm family。保留目录不完整或被篡改时禁止回退系统库。没有 package marker 时，原有显式路径和 system `libmigraphx_c.so.3` 查找仍然可用。详见 [M7 设计](docs/design/m7-runtime-packaging.md)、[部署指南](docs/guides/runtime-deployment.md)与 [M7 验证状态](docs/validation/m7-local-validation.md)。

## 构建

安装 `global.json` 选择的 .NET 10 SDK、PowerShell 7、CMake 和 C 编译器，然后执行：

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\verify-m3-coverage.ps1
.\eng\verify-m4-coverage.ps1
.\eng\verify-m5-coverage.ps1
.\eng\verify-m6-coverage.ps1
.\eng\validate-runtime-manifest.ps1
.\eng\test-runtime-supply-chain.ps1
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\verify-m2-abi.ps1 -AcquireInputs
.\eng\verify-m3-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
$adapter = .\eng\pack-adapter.ps1 -Configuration Release -Version 0.0.0 -HipSharpVersion 0.9.1 -NoBuild
.\eng\verify-adapter-package.ps1 -PackagePath $adapter -Version 0.0.0 -HipSharpVersion 0.9.1
.\eng\docs.ps1 -Configuration Release -NoBuild
```

构建、官方 ELF 静态证据、fake-native 执行与官方 MIGraphX runtime 执行继续是不同证据层级。M1/M2 runtime 结论只适用于[官方验证摘要](docs/validation/m1-m2-official-runtime.md)记录的精确 SHA、环境、模型、shape 和同步 offload-copy 路径。

## 文档

请阅读 [M4 托管对象设计](docs/design/m4-managed-object-model.md)、[M5 动态 Shape 与缓存设计](docs/design/m5-dynamic-shape-cache.md)、[M6 异步/HIP 设计](docs/design/m6-hip-async-interop.md)、[M7 Runtime 设计](docs/design/m7-runtime-packaging.md)、[托管对象指南](docs/guides/managed-objects.md)、[Runtime 部署指南](docs/guides/runtime-deployment.md)、[M7 本地验证](docs/validation/m7-local-validation.md)、[平台证据](docs/compatibility/platforms.md)和[M1/M2 官方验证摘要](docs/validation/m1-m2-official-runtime.md)。

## 许可证

Copyright 2026 Guojin Yan。本托管项目采用 [Apache License 2.0](LICENSE)，归属信息见 [NOTICE](NOTICE)。包中不含 AMD/ROCm/MIGraphX 组件；这些组件继续受各自许可证约束。

## 参与贡献

请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)、[SECURITY.md](SECURITY.md)、[M3 normalized model](compatibility/m3-normalized-api.json) 和 [M6 high-level map](compatibility/m6-high-level-api-map.json)。公开 API 必须提供语义对应的中英文 XML 文档。不得提交 AMD 二进制、fake-native 构建输出、模型、凭据、云端连接信息，或无法由固定输入重现的生成声明。
