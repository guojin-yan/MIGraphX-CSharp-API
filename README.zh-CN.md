<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/readme/hero-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="docs/images/readme/hero-light.svg">
  <img alt="MIGraphXSharp — 面向 C# 与 .NET 的 AMD GPU 推理" src="docs/images/readme/hero-light.svg" width="100%">
</picture>

# MIGraphX C# API · MIGraphXSharp

[![NuGet](https://img.shields.io/nuget/v/JYPPX.ROCm.MIGraphX.CSharp.API.svg)](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/)
[![Downloads](https://img.shields.io/nuget/dt/JYPPX.ROCm.MIGraphX.CSharp.API.svg)](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/)
[![Release](https://img.shields.io/github/v/release/guojin-yan/MIGraphX-CSharp-API)](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-4.6_to_10.0-512BD4)](docs/compatibility/frameworks.md)
[![ROCm](https://img.shields.io/badge/ROCm-7.2.1-ED1C24)](docs/guides/runtime-deployment.md)
[![Stars](https://img.shields.io/github/stars/guojin-yan/MIGraphX-CSharp-API?style=flat)](https://github.com/guojin-yan/MIGraphX-CSharp-API/stargazers)

[English](README.md) | 简体中文

MIGraphXSharp 是 AMD MIGraphX 的 C# / .NET 封装库。通过具有明确生命周期的托管对象，加载 ONNX 模型、编译 AMD GPU 程序、准备类型化张量并读取推理结果；可选 HIP 适配器提供流提交与设备缓冲区输入。

项目提供两个完整视觉案例：**YOLO26 检测、分类、实例分割、姿态、旋转框五任务**，以及 **PP-OCRv6 文本检测与识别**。图像预处理与后处理均使用 C#，运行时无需 Python 或 OpenCV。

[快速开始](#快速开始) · [视觉案例](#视觉案例) · [使用文档](#使用文档) · [发行说明](CHANGELOG.md) · [问题反馈](https://github.com/guojin-yan/MIGraphX-CSharp-API/issues)

## 0.0.1 版本

首个版本提供核心托管包、可选 HIP 适配包、中英文 XML API 文档和两个独立完整的视觉示例项目。软件包、源码与离线文档见 [v0.0.1 Release](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v0.0.1)。

在 Ubuntu 24.04、ROCm 7.2.1、MIGraphX 2.15.0、gfx1100 上，已执行所附视觉模型、动态输入、持久化/缓存、HIP 主机异步与设备输入的官方运行时检查。具体适用范围见[平台支持说明](docs/compatibility/platforms.md)。

## 主要功能

| 功能 | 使用方式 |
| --- | --- |
| ONNX 推理 | 从文件或内存解析、配置输入尺寸、编译目标、执行并读取类型化输出 |
| 张量与资源管理 | 不可变形状快照、类型化主机输入、多输入/输出、显式释放原生资源 |
| 动态形状与缓存 | 设置维度范围、校验具体输入、保存/加载程序、复用编译结果 |
| 图构建 | program 管理的 module、parameter、literal、instruction 与类型化算子属性 |
| HIP 集成 | 使用 HipSharp 流提交、等待或轮询完成，使用经过校验的设备内存输入 |
| 高级接口 | TensorFlow 解析、FP16/BF16/INT8/FP8 量化与实验性自定义算子回调 |
| 视觉案例 | YOLO26 五任务、PP-OCRv6 三种规格；JSON、预览图、mask、文本与计时输出 |
| .NET 接入 | 15 个目标框架、一致的公开 API、中英文 XML 注释及加载诊断 |

## 软件包与运行环境

| 软件包 | 版本 | 用途 |
| --- | --- | --- |
| [JYPPX.ROCm.MIGraphX.CSharp.API](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/0.0.1) | 0.0.1 | 核心推理与托管资源 API |
| [JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop/0.0.1) | 0.0.1 | 可选流/设备输入集成；精确依赖同版本核心包与 HipSharp 0.10.0 |

核心命名空间为 <code>JYPPX.ROCm.MIGraphXSharp</code>。两个包均提供 <code>net46</code>–<code>net481</code>、<code>netcoreapp3.1</code>、<code>net5.0</code>–<code>net10.0</code> 的程序集，共 15 个目标框架。视觉示例使用 .NET 10。

| 组件 | 已验证基线 |
| --- | --- |
| 操作系统 | Ubuntu 24.04 x86-64 |
| AMD 运行环境 | ROCm 7.2.1、MIGraphX 2.15.0.70201-81~24.04 |
| GPU 架构 | gfx1100 |
| 原生库路径 | /opt/rocm/lib/libmigraphx_c.so.3 |

ROCm/MIGraphX 通过 [AMD 官方系统仓库](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html)安装，NuGet 包不携带 AMD 原生运行库。Windows 可构建托管程序集，本项目不提供官方 Windows MIGraphX 运行时。其他设备和运行库版本需按实际应用验证。

## 快速开始

### 1. 安装软件包

~~~bash
dotnet new console -n MigraphxDemo
cd MigraphxDemo
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API --version 0.0.1

# 可选：HIP 流与设备输入集成
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop --version 0.0.1
~~~

Release 同时提供离线包，配置方法见[入门指南](docs/guides/getting-started.md)。

### 2. 执行 ONNX 推理

下面演示一个已有模型：单个静态、标准布局的 float32 输入/输出，输入共四个元素。

~~~csharp
using JYPPX.ROCm.MIGraphXSharp;

var result = MIGraphXOnnxWorkflow.RunFile(
    "/opt/rocm/lib/libmigraphx_c.so.3",
    "/absolute/path/to/model.onnx",
    new[] { 1f, 2f, 3f, 4f });

Console.WriteLine(string.Join(", ", result.Output));
~~~

路径使用绝对路径，输入数量必须与自己的模型一致。此入口启用 GPU host offload-copy，返回托管输出副本。图片模型可直接使用下方案例；多输入、多输出用法见[托管对象指南](docs/guides/managed-objects.md)。

### 3. 检查运行环境

~~~csharp
var report = MIGraphXEnvironment.Probe("/opt/rocm/lib/libmigraphx_c.so.3");
Console.WriteLine(report.State);
foreach (var diagnostic in report.Diagnostics)
    Console.WriteLine(diagnostic);
~~~

原生库定位、版本和部署问题见[部署与故障排查](docs/guides/runtime-deployment.md)。

## 视觉案例

每个案例项目都包含完整的图像、推理、后处理与计时代码，仅引用本库及 ImageSharp，无需单独的公共示例项目。ONNX、图片、标签和 OCR 字典由使用者准备。

| 项目 | 任务 | 典型输入 | 输出 |
| --- | --- | --- | --- |
| [Yolo26](samples/Yolo26/README.md) | 目标检测 | 640 × 640 | 检测框、类别与分数 |
| Yolo26 | 图像分类 | 224 × 224 | Top-5 类别 |
| Yolo26 | 实例分割 | 640 × 640 | 每实例 mask 与检测框 |
| Yolo26 | 人体姿态 | 640 × 640 | 每人 17 个关键点与骨架 |
| Yolo26 | 旋转框检测 | 1024 × 1024 | 旋转四边形 |
| [PaddleOcrV6](samples/PaddleOcrV6/README.md) | 文本检测识别 / 仅识别 | 检测尺寸为 32 的倍数；识别 48 × 320 | 文字、四边形、裁剪和置信度 |

在已经配置运行环境的 Linux 主机上，从仓库根目录执行：

~~~bash
dotnet run --project samples/Yolo26 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 --task detect \
  --model /models/yolo26n.onnx --image /images/bus.jpg \
  --output /results/yolo26 --warmup 5 --iterations 30

dotnet run --project samples/PaddleOcrV6 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --det-model /models/PP-OCRv6_tiny_det_inference.onnx \
  --rec-model /models/PP-OCRv6_tiny_rec_inference.onnx \
  --dictionary /models/PP-OCRv6_tiny_rec_dict.txt \
  --image /images/document.jpg --output /results/ocr
~~~

其他 YOLO 任务更换任务名与配套模型；OCR small/medium 使用同一入口，更换配套模型和字典。省略 detector 参数可直接识别文本行。两个案例均保存 JSON、PNG 和独立 SVG，分割额外保存 mask，OCR 额外保存文字与裁剪。

本次姿态模型在 MIGraphX 2.15.0 下需要启动前设置 <code>MIGRAPHX_DISABLE_REDUCE_FUSION=1</code>。模型权重和演示图片不随库分发。

### 图片实测速度

在 gfx1100、CPU 配额 16 核的环境中顺序测量：batch=1、float32，每组预热 5 次、测量 30 次。下表是指定图片的实际耗时，不是显卡性能排名。

| 模型 / 工作负载 | 推理平均 ms | 流水线平均 ms | 流水线 P95 ms |
| --- | ---: | ---: | ---: |
| YOLO26 n 检测 | 3.23 | 16.82 | 19.94 |
| YOLO26 s 检测 | 4.96 | 17.83 | 22.27 |
| YOLO26 s 分类 | 1.47 | 5.65 | 6.14 |
| YOLO26 s 分割 | 7.88 | 59.05 | 64.76 |
| YOLO26 s 姿态 | 5.43 | 18.06 | 21.23 |
| YOLO26 s 旋转框 | 8.35 | 36.88 | 45.22 |
| PP-OCRv6 tiny / 16 行 | 23.71 | 66.42 | 84.18 |
| PP-OCRv6 small / 16 行 | 58.95 | 102.76 | 133.39 |
| PP-OCRv6 medium / 16 行 | 75.73 | 115.69 | 148.64 |

推理包含同步执行和主机输出读取，OCR 包含检测加全部文本行识别；流水线还包含 C# 预处理、后处理、裁剪和 mask 计算。均排除文件解码、模型解析/编译、绘制与写盘。未锁定 GPU 时钟/功耗，也未清除缓存。自己的模型可按[案例计时参数](docs/guides/vision-samples.md)重新测量。

## 使用文档

| 入门与用法 | 参考资料 |
| --- | --- |
| [安装与首次推理](docs/guides/getting-started.md) | [API 参考](docs/api/index.md) |
| [托管对象与资源所有权](docs/guides/managed-objects.md) | [框架兼容性](docs/compatibility/frameworks.md) |
| [动态输入、持久化与缓存](docs/guides/dynamic-shapes-cache.md) | [平台支持与限制](docs/compatibility/platforms.md) |
| [类型化算子属性](docs/guides/operation-attributes.md) | [版本规则](docs/guides/api-versioning.md) |
| [HIP 异步执行](docs/guides/hip-interop.md) | [部署与故障排查](docs/guides/runtime-deployment.md) |
| [图构建、TensorFlow 与量化](docs/guides/advanced-apis.md) | [视觉案例指南](docs/guides/vision-samples.md) |

Release 附带离线 HTML 文档，解压文档包后打开 <code>index.html</code>。

## 功能边界

常用整数、浮点数、Boolean、字符串、null 和数组属性可通过类型化构建器使用；通过 program 创建或取得的 module 由 program 租约管理生命周期。任意 C 格式化可变参数，以及缺少原生销毁契约的独立 module，不属于公开封装接口。

实验性自定义算子回调已有主机参考目标数值检查，GPU/device 回调内存及完整 provider 生命周期尚未全面验证。库不作零拷贝、并行重叠或任意工作负载性能保证。OCR 使用矩形 unclip，不包含版面分析、页面方向分类或表格识别。接入实验性功能前请阅读平台说明。

## 从源码构建

安装 <code>global.json</code> 指定的 SDK、PowerShell 7，并配置 [HipSharp 源码依赖](CONTRIBUTING.md)，然后执行：

~~~powershell
./eng/build.ps1 -Configuration Release -Version 0.0.1
./eng/pack.ps1 -Configuration Release -Version 0.0.1 -NoBuild
~~~

不指定版本的开发构建使用 <code>0.0.0</code>。构建输出、包和生成文档写入相邻的 <code>MIGraphXSharp.Workspace/</code>，完整环境说明见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 项目结构

| 目录 | 内容 |
| --- | --- |
| src/ | 核心库与可选 HIP 适配器 |
| samples/ | 可直接运行的 C# 示例 |
| docs/ | 使用指南、部署和 API 文档 |
| tests/、native/ | 托管测试与原生测试源码 |
| eng/、tools/ | 构建、打包、生成与诊断脚本 |
| compatibility/ | API 快照、生成器输入和支持范围契约 |

## 交流与许可证

欢迎通过 [GitHub Issues](https://github.com/guojin-yan/MIGraphX-CSharp-API/issues)反馈问题，并附上包版本、原生库版本、系统、GPU 和最小复现。也欢迎提交改进。

相关 .NET 推理项目：[OpenVINO C# API](https://github.com/guojin-yan/OpenVINO-CSharp-API) · [TensorRT C# API](https://github.com/guojin-yan/TensorRT-CSharp-API) · [HIP C# API](https://github.com/guojin-yan/HIP-CSharp-API)。

Copyright 2026 Guojin Yan。项目采用 [Apache-2.0](LICENSE) 许可证，另见 [NOTICE](NOTICE)。AMD、ROCm、MIGraphX、ImageSharp 及模型素材分别适用各自许可证。
