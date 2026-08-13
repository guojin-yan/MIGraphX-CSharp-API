# MIGraphXSharp

[English / 英文](README.md)

MIGraphXSharp 计划为 .NET 提供 AMD 官方 MIGraphX C API 绑定。版本 `0.0.0` 是 M0 工程与研究基线：它建立可复现的多目标构建、包与文档门禁，并冻结上游输入；当前尚未实现任何 MIGraphX 原生调用。

## 状态

- 托管核心精确构建从 `net46` 到 `net10.0` 的 15 TFM 矩阵。
- 冻结的研究目标是 ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`。
- M0 没有 MIGraphX P/Invoke、ONNX parse/compile/run、native loader、异步执行或 GPU 执行。
- 本地机器没有 AMD GPU。未来任何 GPU 声明都必须引用本项目 Radeon Cloud 记录和已推送的 40 位 Git SHA。
- 核心包不携带 MIGraphX 或 ROCm 原生文件。Runtime 包已禁用并采用 fail-closed 策略。
- 项目许可证仍待 Owner 决定，因此该包只是本地工程产物，不得发布。

## 安装

M0 可以生成仅供本地验证的候选包：

冻结的 NuGet 包 ID、核心项目和程序集是 `JYPPX.ROCm.MIGraphX.CSharp.API`；C# 命名空间仍为 `JYPPX.ROCm.MIGraphXSharp`。

```powershell
.\eng\pack.ps1 -Configuration Release -Version 0.0.0
```

不要发布该候选包，也不要把它当作可用的 MIGraphX 绑定。未来核心包仍采用 managed-first 策略，默认依赖独立验证的系统原生安装；只有明确安装经过验证的 RID runtime 包时才改变这一点。

## 使用

当前唯一公开 API 只报告工程状态，绝不探测原生软件：

```csharp
using JYPPX.ROCm.MIGraphXSharp;

Console.WriteLine(MIGraphXBuildInfo.PackageVersion);        // 0.0.0
Console.WriteLine(MIGraphXBuildInfo.NativeBindingsAvailable); // false
```

## 构建

安装与 `global.json` 兼容的 .NET 10 SDK，然后执行：

```powershell
dotnet tool restore
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
.\eng\docs.ps1 -Configuration Release -NoBuild
```

构建成功、包中资产、clean consumer 编译、原生加载和 AMD GPU 执行是不同证据层级。M0 只证明验证报告列出的托管层级。

## 文档

DocFX 源码位于 [`docs`](docs/index.md)。原生研究基线在 [`docs/design/m0-native-baseline.md`](docs/design/m0-native-baseline.md) 中记录官方包 URL、包/头文件/库 SHA-256、API 族计数、SONAME、依赖、导出及证据限制。

## 参与贡献

请阅读 [`CONTRIBUTING.md`](CONTRIBUTING.md)、[`SECURITY.md`](SECURITY.md) 和 [`compatibility`](compatibility/upstream-c-api-manifest.json) 下的机器可读文件。新增公开 API 必须提供语义对应的中英文 XML 文档。不得提交 AMD 二进制、模型、凭据、云端连接信息，或无法由固定输入重现的生成绑定。
