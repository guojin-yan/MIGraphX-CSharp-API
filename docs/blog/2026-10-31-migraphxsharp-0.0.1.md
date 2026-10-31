# MIGraphXSharp 0.0.1 发布：用 C# 在 AMD GPU 上运行 YOLO26 和 PP-OCRv6

作者：Guojin Yan · 2026-10-31 · 适用版本：MIGraphXSharp 0.0.1

## 前言

用 C# 做视觉应用，图像采集、界面、业务逻辑和结果保存通常已经有了自己的工程。接入一个深度学习模型时，我更希望把推理也放进这套工程：输入一张图片，调用模型，再把检测框、文字或分类结果交给后面的业务处理。

如果部署目标换成 AMD GPU，就需要进一步处理 MIGraphX 原生库、C 接口、张量布局和资源释放。模型在其他语言的示例中能够运行，距离在 .NET 应用里稳定地调用，仍然有一段接入工作。

我开发 MIGraphXSharp，就是希望给这段接入工作提供一个可以复用的起点。**MIGraphXSharp 是 AMD MIGraphX 的 C# / .NET 封装库，用托管对象完成 ONNX 模型的解析、编译、执行与结果读取。**

这次发布的版本从 **0.0.1** 开始，核心包和可选 HIP 适配包都已经发布到 NuGet.org。仓库同时提供两个独立的完整案例：YOLO26 五任务，以及 PP-OCRv6 文本检测与识别。

先说明使用条件：本文的真实 GPU 结果来自 **Ubuntu 24.04 x86-64、ROCm 7.2.1、MIGraphX 2.15.0 和 gfx1100**。托管程序集可以在 Windows 上构建，实际推理仍需要匹配的 Linux 原生运行环境。

## 一、这个项目解决什么问题

MIGraphX 是 AMD ROCm 生态中的模型推理引擎，可以解析 ONNX 等模型表示，并为目标设备编译执行程序。MIGraphXSharp 在它的 C 接口之上，提供 C# 开发者可以直接使用的对象和调用方式。

对于应用开发者，常用入口是 `MIGraphXProgram`、`MIGraphXShape`、`MIGraphXArgument` 和 `MIGraphXParameterMap`。模型、输入参数和输出分别对应明确的对象；持有原生资源的对象通过 `IDisposable` 释放，能够放进熟悉的 `using` 作用域。

当前版本围绕模型推理提供了以下能力：

| 环节 | 可以完成的工作 |
| --- | --- |
| 环境检查 | 指定原生库路径，查看加载诊断，检查基础对象调用 |
| 模型准备 | 从文件或内存解析 ONNX，设置输入尺寸，编译 GPU 程序 |
| 输入与输出 | 创建类型化张量，设置模型参数，同步执行并复制输出 |
| 模型复用 | 配置动态维度，保存和加载程序，使用编译缓存 |
| 扩展接入 | 构建图与类型化算子属性，通过可选适配包接入 HIP 流和设备输入 |

如果只是运行普通 ONNX 图片模型，可以先使用核心包。需要与已有 HIP 流或显存管理代码衔接时，再安装 HIP 适配包。TensorFlow 解析、量化和实验性自定义算子也有对应接口，具体使用条件见项目文档。

## 二、NuGet 包与运行环境如何配合

先把托管包、原生运行库和 GPU 的关系理清，后面的安装和排错会容易很多。

![MIGraphXSharp 核心包、可选 HIP 适配包、AMD 系统运行库与 GPU 的关系](assets/migraphxsharp-0.0.1/runtime-stack.png)

图中的核心包负责模型与张量的托管入口，可选适配包负责 HIP 集成；两条路径最终都使用主机上的 AMD 原生运行库。**NuGet 安装成功以后，还需要正确安装驱动、ROCm 和 MIGraphX，才能执行 GPU 推理。**

| 软件包 | 版本 | 用途 |
| --- | --- | --- |
| `JYPPX.ROCm.MIGraphX.CSharp.API` | `0.0.1` | 核心推理 API，无 HipSharp 包依赖 |
| `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` | `0.0.1` | 可选的 HIP 流与设备输入适配 |
| `JYPPX.ROCm.HIP.CSharp.API` | `0.10.0` | HIP 适配包精确依赖的 HipSharp 版本 |

两个 MIGraphX 包都包含 15 个目标框架的程序集：`.NET Framework 4.6` 至 `4.8.1`、`.NET Core 3.1`，以及 `.NET 5` 至 `.NET 10`。下面的控制台示例和视觉案例使用 .NET 10。

本次已验证的原生环境为：

| 组件 | 版本或条件 |
| --- | --- |
| 系统 | Ubuntu 24.04 x86-64 |
| ROCm | 7.2.1 |
| MIGraphX | `2.15.0.70201-81~24.04` |
| GPU 架构 | `gfx1100` |
| 示例原生库路径 | `/opt/rocm/lib/libmigraphx_c.so.3` |

原生组件使用 [AMD 官方系统仓库](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html)安装。本项目的 NuGet 包包含托管程序集和 API 文档；原生运行库由系统提供。其他 GPU、发行版与 ROCm/MIGraphX 版本，需要在目标环境重新验证。

## 三、先运行一个有明确输出的小模型

第一次接入时，我建议先用一个容易判断结果的模型，把原生库加载、NuGet 引用和模型执行连起来，再换成图片模型。

这里使用仓库自带脚本生成的 Identity ONNX：输入和输出都是 `float32[1,4]`，输出应与输入一致。它不需要训练权重，也不需要安装 Python。

### 3.1 准备环境与模型

以下命令在已经配置好 AMD GPU、驱动和上述系统运行库的 Linux 主机上执行。另需安装 .NET 10 SDK 和 PowerShell 7；PowerShell 用于运行仓库内的 ONNX 生成脚本。安装方式分别见 [.NET 文档](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu)和 [PowerShell 文档](https://learn.microsoft.com/powershell/scripting/install/install-ubuntu)。

在一个新的工作目录中执行：

```bash
mkdir migraphx-first-run
cd migraphx-first-run

git clone --branch v0.0.1 --depth 1 \
  https://github.com/guojin-yan/MIGraphX-CSharp-API.git source

pwsh -NoProfile -File ./source/tests/fixtures/generate-onnx-identity.ps1 \
  -OutputPath "$PWD/identity.onnx"

dotnet new console -n Demo --framework net10.0
dotnet add ./Demo/Demo.csproj package JYPPX.ROCm.MIGraphX.CSharp.API --version 0.0.1
```

脚本会生成 `identity.onnx`，并输出模型路径、SHA-256 和输入输出说明。模型放在 `source` 目录外；`Demo` 则通过 NuGet 使用公开的 `0.0.1` 核心包。

### 3.2 编写 C# 调用代码

将 `Demo/Program.cs` 替换为下面的完整代码。第一个命令行参数是原生库绝对路径，第二个是 ONNX 文件绝对路径。

```csharp
using JYPPX.ROCm.MIGraphXSharp;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Demo <native-library-path> <onnx-model-path>");
    Environment.ExitCode = 1;
    return;
}

string nativePath = args[0];
string modelPath = args[1];

var report = MIGraphXEnvironment.Probe(nativePath);
Console.WriteLine($"Native state: {report.State}");
foreach (var diagnostic in report.Diagnostics)
    Console.WriteLine(diagnostic);

if (report.State != "executed")
{
    Environment.ExitCode = 1;
    return;
}

var result = MIGraphXOnnxWorkflow.RunFile(
    nativePath, modelPath, new[] { 1f, 2f, 3f, 4f });

Console.WriteLine($"Output: {string.Join(", ", result.Output)}");
```

`Probe` 检查原生库加载，并执行基础 target/program 对象的创建和释放。随后 `RunFile` 解析模型、为 GPU 编译、同步运行，并把输出复制为托管数据；这个便捷入口内部管理原生资源的释放。

需要注意的是，`RunFile` 便捷入口要求单个静态、标准布局的 float32 输入和输出，输入元素数量也必须匹配。多输入、多输出和更复杂的模型，可以使用[显式托管对象 API](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/guides/managed-objects.md)。

### 3.3 运行并查看输出

仍在 `migraphx-first-run` 目录执行：

```bash
dotnet run --project ./Demo -c Release -- \
  /opt/rocm/lib/libmigraphx_c.so.3 "$PWD/identity.onnx"
```

如果主机使用版本化安装路径，把第一个参数改成实际存在的路径，例如 `/opt/rocm-7.2.1/lib/libmigraphx_c.so.3`。

环境和模型均正常时，关键输出应包含：

```text
Native state: executed
Output: 1, 2, 3, 4
```

能够得到这组 Identity 输出，就完成了一次从 C# 到 MIGraphX 的模型执行。如果只有加载诊断或出现异常，先检查原生库路径、依赖版本和设备访问，再继续接入视觉案例。完整排错说明见[运行环境部署指南](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/guides/runtime-deployment.md)。

## 四、YOLO26 和 PP-OCRv6 案例可以直接看什么

最小模型适合确认接入路径，图片案例则更接近实际业务。当前仓库提供两个独立项目，每个项目都包含自己的图像处理、推理、后处理和计时代码。

| 示例项目 | 当前任务 | 输出内容 |
| --- | --- | --- |
| `samples/Yolo26` | 检测、分类、实例分割、人体姿态、旋转框检测 | 检测结果、Top-5、实例 mask、关键点、旋转四边形，以及 JSON/PNG/SVG |
| `samples/PaddleOcrV6` | PP-OCRv6 tiny/small/medium 整图检测识别、仅识别模式 | 文字、文本框、置信度、文本行裁剪，以及 JSON/TXT/PNG/SVG |

两者的预处理和后处理均使用 C#，图像读写使用 ImageSharp。运行示例无需 Python 或 OpenCV；ONNX 模型、标签、字典和输入图片由使用者准备。

以上一节克隆的 `source` 为起点，准备好对应模型和图片后，可以运行 YOLO26 检测：

```bash
dotnet run --project ./source/samples/Yolo26 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --task detect --model /models/yolo26n.onnx \
  --image /images/bus.jpg --output /results/yolo26 \
  --warmup 5 --iterations 30
```

这里的模型、图片和输出路径需要替换成自己的绝对路径。`result.json` 保存结果和计时，`preview.png` 展示几何结果，`annotated.svg` 还包含文字标签。示例按 `v0.0.1` 源码通过项目引用构建；前面的 `Demo` 演示公开 NuGet 包的使用方式。

YOLO26 示例针对本次使用的端到端 ONNX 导出格式编写。检测结果已经包含候选选择，示例按分数筛选，后处理不再额外执行一遍 NMS，也不能直接套用 YOLOv8 原始检测头的解码方式。

OCR 示例包含文本检测、透视裁剪和 CTC（Connectionist Temporal Classification）识别解码。识别模型必须搭配对应字典；只有文本行图片时，可以省略检测模型，直接运行识别。

完整参数分别见 [YOLO26 示例说明](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/samples/Yolo26/README.md)和 [PP-OCRv6 示例说明](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/samples/PaddleOcrV6/README.md)，后续两篇案例文章再展开预处理、输出布局和解码代码。

## 五、实际运行结果与速度

这次发布前，我在 gfx1100 环境运行了六个 YOLO26 模型、三组 PP-OCRv6 模型和 OCR 仅识别模式，并对 C# 保存的实际输入进行独立 CPU 结果对照。

指定公交车图片中，YOLO26 n/s 检测模型均得到 5 个目标，实例分割得到 4 个实例，姿态模型得到 4 个人。分类 Top-1 为 `minibus`；指定航拍图片得到 12 个飞机旋转框。三种 OCR 规格在同一张产品说明图片上均识别出 16 行文字。

对照引擎使用 ONNX Runtime 1.24.4 CPUExecutionProvider。分割的 4 个实例二值 mask 与 CPU 对照的 IoU（交并比）均为 1；三种 OCR 共 48 条文本行的识别张量和 CTC 解码也通过核对。这些检查用于确认指定输入上的实现结果，单张图片的演示不能替代数据集准确率评估。

速度采用同一图片、batch=1、float32，每组预热 5 次后顺序测量 30 次。环境为 Ubuntu 24.04、ROCm 7.2.1、MIGraphX 2.15.0、gfx1100，CPU 配额 16 核；未锁定 GPU 时钟或调整功耗。

| 模型 / 工作负载 | 推理平均 ms | 流水线平均 ms | 流水线 P95 ms |
| --- | ---: | ---: | ---: |
| YOLO26 n 检测 / 640×640 | 3.23 | 16.82 | 19.94 |
| YOLO26 s 检测 / 640×640 | 4.96 | 17.83 | 22.27 |
| YOLO26 s 分类 / 224×224 | 1.47 | 5.65 | 6.14 |
| YOLO26 s 分割 / 640×640 | 7.88 | 59.05 | 64.76 |
| YOLO26 s 姿态 / 640×640 | 5.43 | 18.06 | 21.23 |
| YOLO26 s 旋转框 / 1024×1024 | 8.35 | 36.88 | 45.22 |
| PP-OCRv6 tiny / 16 行 | 23.71 | 66.42 | 84.18 |
| PP-OCRv6 small / 16 行 | 58.95 | 102.76 | 133.39 |
| PP-OCRv6 medium / 16 行 | 75.73 | 115.69 | 148.64 |

这里需要分清两个计时范围：

- **推理**包含同步 `Program.Run` 和主机输出复制/读取。OCR 的数字包含检测及全部 16 行识别；检测输入为 512×512，每行识别输入为 48×320。
- **流水线**还包含 C# 图像预处理、输入参数创建和后处理。OCR 包含裁剪，分割包含原图尺寸 mask 计算；不包含图片文件读取/解码、模型解析/编译、绘制和写盘。

因此，分割模型推理约 7.88 ms，流水线约 59.05 ms，是两种计时范围下的实际结果。接入应用时，还需要关注图像处理和后处理成本。P95 使用 nearest-rank，即对样本排序后取对应百分位位置；这些数字不能直接换成视频服务或并发吞吐承诺。

数据来自发布前的[示例计时源码](https://github.com/guojin-yan/MIGraphX-CSharp-API/tree/b3024abef13c6d9975602821b88fb12ce0a384b2/samples)，后续独立项目整理保持了计时算法。正式测量的输出也完成了独立 CPU 对照。公共 `0.0.1` 包另完成 NuGet 源还原、托管消费和程序集内容核对；包安装检查与 GPU 实测分别记录。

## 六、当前边界与接下来的使用建议

先从匹配的 Linux 环境和附带示例开始，会更容易确认问题发生在哪一层。Windows 可以用于托管开发和构建，本项目尚未提供官方 Windows MIGraphX 原生推理环境。

这批姿态模型在 MIGraphX 2.15.0 下遇到过 reduce fusion 的 GPU kernel 编译问题，需要在进程启动前设置 `MIGRAPHX_DISABLE_REDUCE_FUSION=1`。升级 MIGraphX 后，应重新检查是否仍需要该设置。

OCR 当前采用矩形 unclip，尚未覆盖完整多边形外扩、页面方向分类、文档去畸变、表格结构和版面分析。CPU/GPU 输出一致，也不表示识别文字已经达到人工逐字校对的准确率。

常用整数、浮点数、Boolean、字符串、null 和数组属性已有类型化入口，program 管理的 module 也可以使用。任意 C 格式化可变参数，以及缺少原生销毁契约的独立 module owner，未进入公开封装范围。实验性自定义算子的 GPU 回调和完整 provider 生命周期仍需进一步验证。

可选 HIP 适配包已针对公开 HipSharp `0.10.0` 完成包引用编译与托管消费检查；此前 GPU 检查使用的是当时冻结的依赖，新的公开包组合尚未重新执行完整 GPU 矩阵。需要使用异步流或设备输入时，请结合 [HIP 集成指南](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/guides/hip-interop.md)评估自己的工作负载。

关于安装、动态输入、模型缓存和两个视觉案例，我会在后续文章中分别展开。这个版本先提供一个可安装、有示例、有实际结果的起点，欢迎带着自己的模型和环境继续验证。

## 七、项目与资料入口

| 资源 | 地址 |
| --- | --- |
| GitHub 仓库 | [MIGraphX-CSharp-API](https://github.com/guojin-yan/MIGraphX-CSharp-API) |
| 核心 NuGet 0.0.1 | [JYPPX.ROCm.MIGraphX.CSharp.API](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/0.0.1) |
| HIP 适配 NuGet 0.0.1 | [JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop/0.0.1) |
| 版本、源码及离线文档 | [GitHub Release v0.0.1](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v0.0.1) |
| 使用文档 | [0.0.1 文档入口](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/index.md) |
| 后续博客文章 | [博客索引](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/main/docs/blog/README.md) |
| 问题反馈 | [GitHub Issues](https://github.com/guojin-yan/MIGraphX-CSharp-API/issues) |

如果运行中出现问题，欢迎在 Issue 中附上包版本、系统、GPU 架构、ROCm/MIGraphX 版本和最小复现。能复现的输入与错误上下文，有助于继续扩大项目的适用范围。

## 文章说明

项目源码采用 Apache-2.0。AMD 运行库、ImageSharp、模型、字典及图片各自适用其许可证，模型和演示原图由使用者另行准备。本文编写使用了 AI 辅助，代码、版本与实测数字依据项目源码和已有运行记录核对；具体应用仍需在自己的设备和数据上验证。

Copyright 2026 Guojin Yan.
