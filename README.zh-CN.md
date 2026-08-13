# MIGraphXSharp

[English / 英文](README.md)

MIGraphXSharp `0.0.0` 现已包含 AMD 官方 MIGraphX C API 的 M1 Direct P/Invoke 纵向子集。它通过一个 manifest，从冻结的 ROCm 7.2.1 头文件绑定六个 target/program create、assign 和 destroy 函数。当前仍是本地工程候选，不是已发布版本。

## 状态

- 核心库精确构建从 `net46` 到 `net10.0` 的 15 TFM；.NET 7+ 使用生成的 `LibraryImport`，旧目标使用生成的 `DllImport`。
- 冻结输入为 ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`，header SHA-256 为 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`。
- M1 提供显式/系统原生加载诊断、精确状态映射、严格 UTF-8，以及内部 target/program SafeHandle；公开诊断面刻意保持最小。
- 本地 fake-native 测试实际执行 loader、export、status、UTF-8、assign、构造失败清理、Dispose 和并发路径。它只是测试替身证据，不是官方 MIGraphX、AMD GPU 或 Radeon Cloud 证据。
- 官方 Linux ELF 导出已静态验证。由于尚无当次 Radeon Cloud 授权，官方 loader 与 target/program runtime 执行仍为 planned。
- ONNX parse、compile、run、argument buffer、shape API、async 和 GPU 推理不属于 M1。
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
```

`ProbeSystem` 会审计应用 RID、应用基目录和系统 loader 候选。Linux 候选包含 `libmigraphx_c.so.3` 与 `migraphx_c`。Windows/macOS 只实现诚实诊断候选，不作官方 MIGraphX runtime 支持声明。

## 构建

安装 `global.json` 选择的 .NET 10 SDK、PowerShell 7、CMake 和 C 编译器，然后执行：

```powershell
dotnet tool restore
.\eng\generate-interop.ps1 -AcquireHeader -Verify
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\verify-m1-abi.ps1 -AcquireInputs
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
.\eng\docs.ps1 -Configuration Release -NoBuild
```

构建、官方 ELF 静态证据、fake-native 执行与官方 MIGraphX runtime 执行是不同证据层级。只有在获得授权的 Radeon Cloud 会话中，针对已推送 40 位 SHA 记录最后一类证据后，M1 才能解除 blocked。

## 文档

请阅读 [M1 Direct P/Invoke 设计](docs/design/m1-direct-pinvoke.md)、[上手指南](docs/guides/getting-started.md)、[平台证据](docs/compatibility/platforms.md)和[验证摘要](docs/validation/README.md)。[M0 基线](docs/design/m0-native-baseline.md)继续作为输入事实记录。

## 许可证

Copyright 2026 Guojin Yan。本托管项目采用 [Apache License 2.0](LICENSE)，归属信息见 [NOTICE](NOTICE)。包中不含 AMD/ROCm/MIGraphX 组件；这些组件继续受各自许可证约束。

## 参与贡献

请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)、[SECURITY.md](SECURITY.md) 和机器可读的 [M1 subset manifest](compatibility/m1-binding-subset.json)。公开 API 必须提供语义对应的中英文 XML 文档。不得提交 AMD 二进制、fake-native 构建输出、模型、凭据、云端连接信息，或无法由固定输入重现的生成声明。
