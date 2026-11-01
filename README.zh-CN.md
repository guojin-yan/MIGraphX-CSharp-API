<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/readme/hero-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="docs/images/readme/hero-light.svg">
  <img alt="MIGraphXSharp — 面向 C# 与 .NET 的 AMD GPU 推理" src="docs/images/readme/hero-light.svg" width="100%">
</picture>

<h1 align="center">MIGraphX C# API · MIGraphXSharp</h1>

<p align="center">用 C# 在 AMD GPU 上运行 ONNX 模型，结合 MIGraphX 推理与 HIP 图像处理。</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache--2.0-blue.svg" alt="Apache-2.0 许可证"></a>
  <a href="https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/"><img src="https://img.shields.io/nuget/v/JYPPX.ROCm.MIGraphX.CSharp.API.svg" alt="NuGet 版本"></a>
  <a href="https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/"><img src="https://img.shields.io/nuget/dt/JYPPX.ROCm.MIGraphX.CSharp.API.svg" alt="NuGet 下载量"></a>
  <a href="https://github.com/guojin-yan/MIGraphX-CSharp-API/releases"><img src="https://img.shields.io/github/v/release/guojin-yan/MIGraphX-CSharp-API" alt="GitHub Release"></a>
  <a href="https://github.com/guojin-yan/MIGraphX-CSharp-API/stargazers"><img src="https://img.shields.io/github/stars/guojin-yan/MIGraphX-CSharp-API?style=flat&amp;logo=github" alt="GitHub Stars"></a>
  <a href="docs/compatibility/frameworks.md"><img src="https://img.shields.io/badge/.NET-4.6_to_10.0-512BD4" alt=".NET 4.6 至 10.0"></a>
  <a href="docs/guides/runtime-deployment.md"><img src="https://img.shields.io/badge/ROCm-7.2.1-ED1C24" alt="ROCm 7.2.1"></a>
</p>

<p align="center"><a href="README.md">English</a> | <strong>简体中文</strong></p>

## 📖 项目简介

[AMD MIGraphX](https://rocm.docs.amd.com/projects/AMDMIGraphX/en/latest/) 是 ROCm 生态中的图推理引擎，能够将深度学习模型编译为 GPU 程序，并针对 AMD 硬件优化执行过程。

**MIGraphX C# API 是 AMD MIGraphX 的 .NET 封装库，让 C# 开发者直接在应用中完成 ONNX 模型加载、GPU 编译、张量操作和模型推理。** 通过可选的 HIP 适配器，还可以在同一条 GPU 流上衔接前处理、模型推理与后处理。

项目提供 **YOLO26** 与 **PP-OCRv6** 完整案例，包含图像处理、推理、结果解析和耗时统计。案例由 C# 与 HIP 实现，运行时无需 Python 或 OpenCV。

| 核心优势 | 能为项目带来什么 |
| --- | --- |
| 🚀 AMD GPU 推理 | 通过 C# 托管 API 使用 MIGraphX 的模型编译与推理能力 |
| ⚡ GPU 图像流水线 | 利用 HIP 流与显存缓冲区，在 GPU 上完成数值前后处理 |
| 🧩 .NET 项目集成 | 覆盖 .NET Framework 4.6 到 .NET 10 的 15 个目标框架，提供中英文 XML 注释 |
| 📚 完整应用案例 | YOLO26 五任务、PP-OCRv6 检测与识别，支持 OCR 多 batch 和多识别实例 |

## 📢 0.0.2 更新摘要

- 新增 <code>RunDeviceIntoAsync</code>，支持显存输出缓冲区与后续 HIP 处理。
- 两个视觉案例的数值前处理、后处理迁移至 HIP。
- OCR 新增批量识别与多实例，整图识别默认 batch 4、2 个识别实例。

[查看 0.0.2 完整版本说明](docs/release-notes/0.0.2.md) · [查看全部版本记录](docs/release-notes/README.md) · [下载 v0.0.2](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v0.0.2)

## 🚀 快速上手

### 1. 安装 NuGet 包

先在兼容的 Linux 主机安装 ROCm 7.2.1 与 MIGraphX 2.15.0，具体见[运行环境部署指南](docs/guides/runtime-deployment.md)，然后创建应用：

~~~bash
dotnet new console -n MigraphxDemo
cd MigraphxDemo
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API --version 0.0.2
~~~

### 2. 编写推理代码

下面使用一个静态 float32 ONNX 模型，输入名为 <code>input</code>、包含 4 个元素。实际接入时，请替换为自己模型的路径、输入名称和数据。

~~~csharp
using JYPPX.ROCm.MIGraphXSharp;

const string nativePath = "/opt/rocm/lib/libmigraphx_c.so.3";

// 解析模型并读取输入形状。
using var options = new MIGraphXOnnxOptions(nativePath);
using var program = MIGraphXProgram.ParseOnnxFile("/models/model.onnx", options);
var inputShapes = program.GetParameterShapes();

// 编译一次，后续输入复用同一个 program。
using var target = new MIGraphXTarget(nativePath, "gpu");
using var compileOptions = new MIGraphXCompileOptions(nativePath, offloadCopy: true);
program.Compile(target, compileOptions);

// 设置模型输入并执行推理。
using var input = MIGraphXArgument.Create(
    nativePath, inputShapes["input"], new[] { 1f, 2f, 3f, 4f });
using var parameters = new MIGraphXParameterMap(nativePath);
parameters.Add("input", input);

using var outputs = program.Run(parameters);
float[] result = outputs[0].ToArray<float>();
Console.WriteLine(string.Join(", ", result));
~~~

### 3. 运行程序

~~~bash
dotnet run -c Release
~~~

图像模型可以直接参考 [YOLO26 案例](samples/Yolo26/README.md)或 [PP-OCRv6 案例](samples/PaddleOcrV6/README.md)。安装与原生库加载诊断见[入门指南](docs/guides/getting-started.md)。

## ✨ 完整特性列表

| 特性 | 说明 |
| --- | --- |
| ONNX 推理 | 从文件或内存解析模型、编译 GPU 程序，支持多输入与多输出 |
| 类型化张量 | 查看形状、类型与步长，创建类型化主机输入，将结果复制为 C# 数组 |
| 动态形状 | 解析前设置固定输入尺寸或动态维度范围 |
| 程序复用 | 保存、加载编译程序，使用显式模型缓存 |
| HIP 集成 | 主机/设备输入异步提交、流同步、调用方显存输出缓冲区 |
| 图构建 API | program 所属 module、参数、常量、指令与类型化算子属性 |
| 高级 API | TensorFlow 解析、FP16/BF16/INT8/FP8 量化与实验性自定义算子回调 |
| 资源管理 | 确定性 <code>Dispose</code>、原生库加载诊断与 HIP 在途任务资源保活 |

各项功能的运行环境要求与实验性功能边界见[使用文档](docs/index.md)和[平台支持说明](docs/compatibility/platforms.md)。

## 📦 NuGet 包

| 包 | 用途 | 版本 |
| --- | --- | --- |
| <code>JYPPX.ROCm.MIGraphX.CSharp.API</code> | 模型、张量、编译与推理核心 API | [![NuGet](https://img.shields.io/nuget/v/JYPPX.ROCm.MIGraphX.CSharp.API.svg)](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/) |
| <code>JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop</code> | 可选 HIP 流与显存适配器 | [![NuGet](https://img.shields.io/nuget/v/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.svg)](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop/) |
| <code>JYPPX.ROCm.HIP.CSharp.API</code> | HipSharp 运行时、显存、流与 kernel API；由适配包依赖 | [![NuGet](https://img.shields.io/nuget/v/JYPPX.ROCm.HIP.CSharp.API.svg)](https://www.nuget.org/packages/JYPPX.ROCm.HIP.CSharp.API/) |

需要 HIP 集成时，再安装适配包：

~~~bash
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop --version 0.0.2
~~~

适配包精确依赖核心包 <code>0.0.2</code> 与 HipSharp <code>0.10.0</code>，由 NuGet 恢复依赖。核心命名空间为 <code>JYPPX.ROCm.MIGraphXSharp</code>。AMD 原生库需单独安装。[GitHub Releases](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases) 同时提供 NuGet 包、源码和离线 HTML 文档。

## 🧪 应用案例

| 案例 | 支持任务 | 包含内容 |
| --- | --- | --- |
| [YOLO26](samples/Yolo26/README.md) | 目标检测、图像分类、实例分割、姿态估计、旋转框检测 | HIP 缩放与归一化、输出解码、mask、关键点和结果标注 |
| [PP-OCRv6](samples/PaddleOcrV6/README.md) | tiny/small/medium 文本检测与识别、仅识别 | HIP 文本框提取、透视裁剪、多 batch、多实例与 CTC 解码 |

两个项目各自包含完整代码，目标框架为 .NET 10，提供 JSON/PNG/SVG 输出、预热与重复计时，并保留 CPU 前后处理选项。两种后端均使用 MIGraphX 执行 GPU 模型推理。模型、输入图片与 OCR 字典由使用者另行准备。

在文档所列 gfx1100、FP32 环境中，HIP 整图平均耗时为：**YOLO26n 检测 2.75 ms，PP-OCRv6 tiny 识别 16 行文本 10.97 ms**。计时不含文件解码、编译、绘图与导出。完整数据和测试条件见 [0.0.2 版本说明](docs/release-notes/0.0.2.md#性能参考)，运行参数见[视觉案例指南](docs/guides/vision-samples.md)。

## 📖 详细文档

| 资源 | 内容 |
| --- | --- |
| [快速入门](docs/guides/getting-started.md) | 安装与首次推理 |
| [托管对象](docs/guides/managed-objects.md) | 模型、张量、输入输出与资源所有权 |
| [动态形状与缓存](docs/guides/dynamic-shapes-cache.md) | 输入尺寸、程序持久化与缓存复用 |
| [HIP 集成](docs/guides/hip-interop.md) | 异步执行与显存缓冲区 |
| [高级 API](docs/guides/advanced-apis.md) | 图构建、TensorFlow 与量化 |
| [API 参考](docs/api/index.md) | 接口文档与离线参考 |
| [版本记录](docs/release-notes/README.md) | 各版本变更、兼容性与升级方法 |
| [技术博客](docs/blog/README.md) | 项目介绍与应用文章 |

## 🚀 更多使用示例

### 指定模型输入尺寸

在解析模型前设置输入维度，后续按上面的对象 API 编译与执行。

~~~csharp
using var options = new MIGraphXOnnxOptions(nativePath);
options.SetInputParameterShape("images", new long[] { 1, 3, 640, 640 });
using var program = MIGraphXProgram.ParseOnnxFile("/models/yolo26n.onnx", options);
~~~

### 保存编译程序

~~~csharp
using var fileOptions = new MIGraphXFileOptions(nativePath, "msgpack");
program.Save("/models/compiled.msgpack", fileOptions);
using var restored = MIGraphXProgram.Load("/models/compiled.msgpack", fileOptions);
~~~

复用程序时需保持兼容的原生运行时与编译目标。显存输出与 GPU 后处理的使用方法见 [HIP 指南](docs/guides/hip-interop.md)。

## 💻 系统要求

| 组件 | 要求 |
| --- | --- |
| 托管类库 | .NET Framework 4.6–4.8.1、.NET Core 3.1、.NET 5–10，共 15 个目标框架 |
| 已验证的原生平台 | Ubuntu 24.04 x86-64、ROCm 7.2.1、MIGraphX 2.15.0、gfx1100 |
| 原生库 | <code>/opt/rocm/lib/libmigraphx_c.so.3</code>，或实际 ROCm 安装中的绝对路径 |
| 视觉案例 | .NET 10；匹配 GPU 架构的 HIPRTC，或预编译 HIP 模块 |

托管框架兼容性不代表提供 Windows/macOS MIGraphX 原生运行时。请按 [AMD 安装说明](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html)与[部署指南](docs/guides/runtime-deployment.md)准备目标设备，其他 GPU/运行时组合见[平台支持说明](docs/compatibility/platforms.md)。

## 🏗️ 项目结构

~~~text
MIGraphXSharp/
├── src/
│   ├── JYPPX.ROCm.MIGraphX.CSharp.API/              # 核心类库
│   └── JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop/  # HIP 适配器
├── samples/
│   ├── Yolo26/                                    # 五类视觉任务
│   └── PaddleOcrV6/                               # 文本检测与识别
├── docs/
│   ├── guides/                                    # 安装与使用
│   ├── release-notes/                             # 版本记录
│   └── blog/                                      # 技术文章
├── eng/                                           # 构建与打包脚本
└── MIGraphXSharp.sln
~~~

## 🔧 从源码构建

安装 <code>global.json</code> 指定的 SDK、PowerShell 7，并配置 [HipSharp 源码依赖](CONTRIBUTING.md)，在仓库根目录执行：

~~~powershell
./eng/build.ps1 -Configuration Release -Version 0.0.2
./eng/pack.ps1 -Configuration Release -Version 0.0.2 -NoBuild
~~~

不指定版本的构建使用 <code>0.0.0</code>，输出写入相邻的 <code>MIGraphXSharp.Workspace/</code> 目录。完整构建配置见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 🤝 如何贡献

欢迎提交 Issue 和 Pull Request。[反馈问题](https://github.com/guojin-yan/MIGraphX-CSharp-API/issues)时，请附上包版本、ROCm/MIGraphX 版本、操作系统、GPU 和最小复现代码。

## 📄 许可证与致谢

本项目采用 [Apache-2.0](LICENSE) 开源协议，另见 [NOTICE](NOTICE)。感谢 [AMD MIGraphX](https://github.com/ROCm/AMDMIGraphX) 与 [ROCm](https://github.com/ROCm) 提供原生推理和 GPU 计算能力。第三方库与模型素材分别适用各自许可证。

相关项目：[OpenVINO C# API](https://github.com/guojin-yan/OpenVINO-CSharp-API) · [TensorRT C# API](https://github.com/guojin-yan/TensorRT-CSharp-API) · [HIP C# API](https://github.com/guojin-yan/HIP-CSharp-API)。

## 📮 联系与赞助

- GitHub：[@guojin-yan](https://github.com/guojin-yan)
- NuGet：[JYPPX.ROCm.MIGraphX.CSharp.API](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/)

<p align="center">
  <img src="docs/images/readme/contact-support-zh.png" width="100%" alt="开发者联系方式、社区入口与赞助二维码">
</p>

## ⚖️ 软件声明与使用说明

本项目在开发过程中使用了 AI 辅助，欢迎通过反馈问题、提交改进共同完善。软件按 Apache-2.0 协议提供，不附带担保；投入实际项目前，请结合自己的模型、原生运行时和部署环境评估。已知限制及实验性 API 范围见[平台支持说明](docs/compatibility/platforms.md)。

Copyright © 2026 Guojin Yan。
