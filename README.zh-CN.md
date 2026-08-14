# MIGraphXSharp

[English / 英文](README.md)

MIGraphXSharp `0.0.0` 现已包含 AMD 官方 MIGraphX C API 的 M1 生命周期基础、M2 受限 ONNX 闭环与 M3 可复现低层绑定生产线。完整冻结头 inventory 已闭合分类，158 个非可变参数声明由同一个 normalized model 生成。当前仍是本地工程候选，不是已发布版本。

## 状态

- 核心库精确构建从 `net46` 到 `net10.0` 的 15 TFM；.NET 7+ 使用生成的 `LibraryImport`，旧目标使用生成的 `DllImport`。
- 冻结输入为 ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`，header SHA-256 为 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`。
- M1 提供显式/系统原生加载诊断、精确状态映射、严格 UTF-8，以及 target/program 生命周期。
- M2 新增显式文件与字节 buffer ONNX 入口，仅支持一个静态、连续的 float32 输入/输出、同步 GPU target compile、offload-copy、固定输入与复制输出。
- M3 盘点 159 个函数、2 个 enum、25 个 opaque handle 和 6 个 callback。192 个实体按 generated/handwritten-policy/unsupported/configuration-unavailable 闭合为 144/47/1/0；函数分类为 117/41/1/0。
- 一个 normalized model 同源生成各 158 个 `LibraryImport` 与 `DllImport` EntryPoint。C 可变参数函数 `migraphx_operation_create` 被显式标为 unsupported，不猜测 ABI。
- 固定头中的 159 个函数全部匹配 hash 校验后的官方 ELF；ELF 额外的私有测试导出单独分类。这些 M3 结论属于 `statically-verified`，不是官方 runtime 执行。
- 本地 fake-native 测试实际执行 loader、frontend/export 分类、parse、shape 校验、compile、run、失败清理和并发路径。它继续作为测试替身证据，与官方 runtime 证据分开记录。
- M1/M2 官方 runtime 已在 `f1a11cfd1701a041cee29188f7600c85b34ae260` 通过：环境为 Ubuntu 24.04 x86-64、ROCm 7.2.1、固定 MIGraphX 包和一张 gfx1100 GPU；实际执行了官方 loader、target/program 生命周期、ONNX file/buffer parse、GPU compile、同步 run 和 Identity reference 对比。
- M3 不新增公开 `Program`、`Shape`、`Argument`、`Target`、options 或集合对象模型。动态 shape、通用多 tensor 工作流、async/stream/device buffer 和 runtime NuGet 仍不在范围内。
- 核心包不含 AMD 或 fake-native 二进制。Runtime 包继续禁用并 fail closed。

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
```

`ProbeSystem` 会审计应用 RID、应用基目录和系统 loader 候选。Linux 候选包含 `libmigraphx_c.so.3` 与 `migraphx_c`。Windows/macOS 只实现诚实诊断候选，不作官方 MIGraphX runtime 支持声明。

## 构建

安装 `global.json` 选择的 .NET 10 SDK、PowerShell 7、CMake 和 C 编译器，然后执行：

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\verify-m2-abi.ps1 -AcquireInputs
.\eng\verify-m3-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
.\eng\docs.ps1 -Configuration Release -NoBuild
```

构建、官方 ELF 静态证据、fake-native 执行与官方 MIGraphX runtime 执行继续是不同证据层级。M1/M2 runtime 结论只适用于[官方验证摘要](docs/validation/m1-m2-official-runtime.md)记录的精确 SHA、环境、模型、shape 和同步 offload-copy 路径。

## 文档

请阅读 [M3 绑定生产线设计](docs/design/m3-binding-generator.md)、[M3 本地验证](docs/validation/m3-local-validation.md)、[上手指南](docs/guides/getting-started.md)、[平台证据](docs/compatibility/platforms.md)和[M1/M2 官方验证摘要](docs/validation/m1-m2-official-runtime.md)。

## 许可证

Copyright 2026 Guojin Yan。本托管项目采用 [Apache License 2.0](LICENSE)，归属信息见 [NOTICE](NOTICE)。包中不含 AMD/ROCm/MIGraphX 组件；这些组件继续受各自许可证约束。

## 参与贡献

请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)、[SECURITY.md](SECURITY.md)、[M3 normalized model](compatibility/m3-normalized-api.json) 和 [coverage summary](compatibility/m3-coverage-summary.json)。公开 API 必须提供语义对应的中英文 XML 文档。不得提交 AMD 二进制、fake-native 构建输出、模型、凭据、云端连接信息，或无法由固定输入重现的生成声明。
