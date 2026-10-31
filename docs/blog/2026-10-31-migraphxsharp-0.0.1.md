# MIGraphXSharp 0.0.1 首发：让 C# 接入 AMD GPU 原生推理

![MIGraphXSharp 0.0.1 首发题图：让 C# 接入 AMD GPU 原生推理](assets/migraphxsharp-0.0.1/cover.png)

作者：Guojin Yan · 首发版本：0.0.1 · 2026-10-31 · 本文修订：2026-11-01

## 前言

用 C# 做工业视觉、图像分析或 AI 服务，模型之外的大部分工程往往已经在 .NET 中：相机采集、设备通信、界面交互、任务调度、数据库和业务规则。模型训练完成以后，接下来要解决的，就是怎样把推理自然地接进这套系统。

硬件选择也越来越重要。部署设备使用 AMD GPU 时，我们需要一条面向 AMD 推理生态的 C# 调用路径。模型在 Python 示例中运行成功以后，C# 应用还要处理原生接口、张量布局、模型编译、动态库加载和资源释放。这些工作如果在每个项目里重新做一遍，会占去不少本该用来开发应用的时间。

此前，我一直在做 OpenVINO、TensorRT 和 HIP 的 C# 接入。沿着这条方向，这次把 AMD MIGraphX 的模型推理能力也接到了 .NET 中。

**MIGraphXSharp 是面向 C#/.NET 的 AMD MIGraphX 封装库，提供 ONNX 模型解析、GPU 编译、推理执行、类型化张量和原生资源管理。** 模型优化与执行由 AMD 原生引擎完成，应用侧使用 C# API 组织输入、调用推理并读取结果。

这是项目的第一次公开发布，版本从 **0.0.1** 开始。核心包、可选 HIP 适配包已经发布到 NuGet.org，仓库提供使用文档、两个独立的完整视觉案例，以及对应的真实 GPU 运行结果。

本文的原生 GPU 实测环境为 **Ubuntu 24.04 x86-64、ROCm 7.2.1、MIGraphX 2.15.0 和 gfx1100**。Windows 可以用于托管开发和构建；实际推理需要匹配的原生运行环境，当前没有提供官方 Windows MIGraphX Runtime。

## 一、项目定位：为 .NET 增加一条 AMD 推理路径

### 1.1 MIGraphX 负责什么，MIGraphXSharp 负责什么

MIGraphX 是 AMD ROCm 生态中的模型推理引擎，可以解析模型计算图，并针对目标设备进行编译和执行。对于已有 ONNX 模型的开发者，它提供了一条从模型表示进入 AMD GPU 的路径。

MIGraphXSharp 建立在 MIGraphX 的 C 接口之上，把应用需要的模型、编译选项、张量、参数和输出组织为托管 API。开发者可以继续使用熟悉的 .NET 项目、类型系统、异常处理和 `using` 作用域。

两者的职责可以概括为：**MIGraphX 执行模型，MIGraphXSharp 让 C# 应用能够清楚地表达这次执行。** 底层引擎决定算子支持和设备执行方式，托管层负责接入、参数检查、资源生命周期与结果读取。

### 1.2 这个项目的实际意义

首先，现有 C# 业务系统可以围绕自己的应用结构接入 AMD 推理。图像采集、业务处理和推理调用能够放进同一个 .NET 工程，开发者可以把更多精力放到模型输入输出和业务逻辑上。

其次，.NET 模型部署可以根据目标硬件评估不同路径。OpenVINO、TensorRT 和 MIGraphX 各有适用的软硬件条件；MIGraphXSharp 为其中的 AMD 原生推理路线提供了可以安装、查看源码和实际运行的 C# 入口。选择哪条路径，应由设备、模型兼容性和应用实测来决定。

另外，模型推理与底层 GPU 编程也有了更清楚的分工。使用 HIP 可以管理设备、显存、Stream 和 Kernel；使用 MIGraphXSharp，则可以从 ONNX 模型直接进入解析、编译和执行。需要把模型推理放进已有 HIP 工作流时，再接入可选适配包。

我希望这个项目带来的价值，是让使用 AMD GPU 的 .NET 开发者少重复一遍原生接入工作，并能把模型调用持续维护在自己的工程里。

## 二、六个核心特色，围绕真实工程接入设计

![MIGraphXSharp 六项工程特色：类型化 API、资源归属、编译复用、独立核心包、加载诊断与 .NET 兼容](assets/migraphxsharp-0.0.1/feature-map.png)

### 2.1 类型化 API，把模型、张量和参数说清楚

推理接入经常会遇到这样的错误：输入名称写错了，`float32` 数据配上了其他类型，或者数组元素数量与模型尺寸不一致。这些问题如果一直传到原生执行阶段，排查会变得困难。

MIGraphXSharp 使用 `MIGraphXProgram` 表达模型程序，使用 `MIGraphXShape` 描述张量类型、尺寸和布局，通过 `MIGraphXArgument`、`MIGraphXParameterMap` 组织输入，通过输出集合读取结果。常见的输入类型和形状不匹配可以在托管调用边界被发现。

对应用代码来说，这些类型提供了明确的阅读线索：哪个对象是模型，哪个是输入数据，哪个是参数表，结果又以什么类型读取。需要多输入、多输出时，也可以沿着同一套显式对象 API 继续展开。

### 2.2 原生资源有明确归属，融入 C# 的 using 习惯

原生程序、参数对象和输出集合都有自己的生命周期。只知道“函数能调通”，还不足以支撑一个长期运行的服务。

项目对拥有原生资源的对象提供 `IDisposable`，调用方可以使用 `using` 安排确定的释放顺序。由 program 管理的 module 等借用视图会保留父对象的生命周期，避免父资源先释放后继续访问借用对象。Shape、名称和同步输出则按各自的契约形成独立快照或副本。

这使资源关系可以直接体现在 C# 代码中。开发者仍然需要遵守同步与异步执行规则，但不用在业务层到处维护裸指针和销毁函数的对应关系。

### 2.3 从一次调用，走向可复用的模型程序

模型解析和 GPU 编译通常属于准备阶段，重复推理属于执行阶段。把这些阶段分开，应用才容易控制启动时间和单次请求的成本。

当前版本支持 ONNX 输入尺寸覆盖、动态维度范围、程序保存与加载，以及编译模型缓存。缓存身份包含模型内容、原生环境指纹、目标、编译选项和托管构建身份等信息，用于区分不同运行条件下的程序产物。

对于固定模型的服务，可以持有已经编译的 program 反复执行；对于需要缓存的应用，可以显式指定缓存目录，并在模型或环境发生变化后重新构建。程序持久化仍受原生版本和编译上下文约束，不能把同一份产物当作跨设备、跨版本的通用文件。

### 2.4 核心包独立，HIP 集成按需加入

普通 ONNX 推理可以只安装核心包，**核心包没有 HipSharp 包依赖**。这让第一次接入时需要理解的对象和依赖更少。

已有 HIP 计算代码的项目，可以再安装 `HIP.Interop` 适配包，把推理提交到 HipSharp Stream，或者使用经过检查的设备缓冲区作为输入。异步执行期间，相关资源会被保留到完成边界；结果按接口契约复制回主机。

这样的组织方式给应用保留了选择：简单场景从同步推理开始，复杂场景再引入 Stream 和设备内存管理。设备输入和异步提交本身并不等于零拷贝或并发加速，最终收益仍取决于实际流水线。

### 2.5 加载与部署可以诊断，问题有明确上下文

“NuGet 已安装，但原生库加载失败”是这类项目中很常见的情况。实际原因可能是路径、依赖动态库、版本组合或设备访问条件，需要有信息才能定位。

MIGraphXSharp 要求明确选择原生库路径，并提供 `MIGraphXEnvironment.Probe` 等诊断入口。应用可以输出当前检查状态和结构化诊断信息，先判断原生加载及基础对象调用是否正常，再继续检查模型执行。

原生运行库由用户按部署环境安装，托管库不会隐式下载一套 Runtime，也不会替应用修改进程的库搜索路径。开发和部署时，实际使用哪套原生环境更容易追踪。

### 2.6 兼顾已有 .NET 工程，附带完整 C# 案例

两个包都提供 15 个目标框架的程序集，范围包括 `.NET Framework 4.6—4.8.1`、`.NET Core 3.1` 和 `.NET 5—10`，并提供中英文 XML API 注释。已有工程可以根据自己的目标框架评估接入方式。

**目标框架兼容与 GPU 平台支持需要分开理解。** 旧 .NET Framework 程序集可以构建和引用，不代表 Windows 已具备可运行的 MIGraphX 原生环境。本文后面的模型执行与速度结果均来自上述 Linux GPU 环境。

仓库另外提供 YOLO26 和 PaddleOcrV6 两个独立项目。各自包含完整的图像处理、推理、后处理和计时代码，便于直接阅读或移入业务工程；运行示例无需 Python 或 OpenCV，图像读写使用 ImageSharp。

## 三、核心架构：托管包、原生引擎与 GPU 如何配合

![MIGraphXSharp 架构：C# 应用通过 Core 调用 MIGraphX，可选 HIP 适配连接流与设备输入，系统提供 ROCm 和驱动](assets/migraphxsharp-0.0.1/runtime-stack.png)

这张图需要读清两层责任：上半部分是项目提供的 .NET 接入层，下半部分是主机上的 AMD 原生运行环境。**NuGet 还原成功以后，还需要安装匹配的驱动、ROCm 和 MIGraphX，才能执行 GPU 推理。**

### 3.1 本次发布的软件包

| 软件包 | 版本 | 用途 |
| --- | --- | --- |
| `JYPPX.ROCm.MIGraphX.CSharp.API` | `0.0.1` | 核心推理与托管资源 API |
| `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` | `0.0.1` | 可选的 HIP Stream 与设备输入适配 |
| `JYPPX.ROCm.HIP.CSharp.API` | `0.10.0` | HIP 适配包精确依赖的 HipSharp 版本 |

MIGraphXSharp 的两个包提供托管程序集和 API 文档，AMD 原生运行库由系统单独提供。核心 API 的命名空间为 `JYPPX.ROCm.MIGraphXSharp`。本次 Release 同时提供源码、包和离线文档入口。

### 3.2 已验证的原生环境

| 组件 | 版本或条件 |
| --- | --- |
| 操作系统 | Ubuntu 24.04 x86-64 |
| ROCm | 7.2.1 |
| MIGraphX | `2.15.0.70201-81~24.04` |
| GPU 架构 | `gfx1100` |
| 示例原生库路径 | `/opt/rocm/lib/libmigraphx_c.so.3` |

其他 GPU、发行版和 ROCm/MIGraphX 版本，需要在目标环境确认模型兼容性与实际执行结果。AMD 官方安装说明可以复制以下地址查看：

```text
https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html
```

## 四、安装与快速开始：先拿到一组确定的输出

先完成一次容易判断结果的调用，再换成自己的模型，会更容易定位接入问题。这里使用 Identity ONNX：输入和输出均为 `float32[1,4]`，预期输出与输入相同。

以下命令在已经配置好上述 GPU 和原生环境的 Linux 主机执行，需要 .NET 10 SDK、Git 和 PowerShell 7。PowerShell 仅用于运行仓库附带的 ONNX 生成脚本，无需安装 Python。

### 4.1 准备模型并安装 NuGet 包

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

脚本会生成 `identity.onnx` 并输出路径、SHA-256 和模型输入输出说明。`Demo` 从公开 NuGet 源引用核心包，暂时不需要安装 HIP 适配包。

### 4.2 编写一次完整的 C# 调用

将 `Demo/Program.cs` 替换为下面的完整代码。两个命令行参数分别是原生库和 ONNX 文件的绝对路径。

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

`Probe` 检查原生库加载，并执行基础 target/program 对象的创建和释放。`RunFile` 随后解析 ONNX、为 GPU 编译、同步运行，并返回托管输出副本；这个便捷入口内部管理原生资源。

这里的 `RunFile` 只用于单个静态、标准布局的 float32 输入和输出，输入元素数量必须匹配。多输入、多输出和更复杂的生命周期控制，使用显式对象 API。

### 4.3 执行并查看成功信号

仍在 `migraphx-first-run` 目录执行：

```bash
dotnet run --project ./Demo -c Release -- \
  /opt/rocm/lib/libmigraphx_c.so.3 "$PWD/identity.onnx"
```

如果采用版本化安装目录，将第一个参数替换为实际路径，例如 `/opt/rocm-7.2.1/lib/libmigraphx_c.so.3`。关键输出应包含：

```text
Native state: executed
Output: 1, 2, 3, 4
```

到这里，就完成了从 C#、NuGet 包、原生库到模型执行的调用。`Probe` 成功只说明其检查范围通过，最终仍要以模型执行结果判断 GPU 推理是否成功。

暂时没有 AMD GPU 时，可以先在本地阅读 API、安装包并构建托管工程，模型执行则留到匹配的 Linux GPU 主机；本地构建通过不能替代设备运行结果。

.NET 与 PowerShell 的 Linux 安装资料：

```text
https://learn.microsoft.com/dotnet/core/install/linux-ubuntu
https://learn.microsoft.com/powershell/scripting/install/install-ubuntu
```

## 五、完整案例：从模型调用到可用的视觉结果

图片推理接入还要处理缩放、归一化、输出解码、坐标还原和结果保存。因此，本次首发同时提供两个完整案例，让读者能沿着一条应用数据流查看代码。

![C# 视觉推理数据流：图像输入、预处理、MIGraphX 执行、后处理与业务输出，覆盖 YOLO26 五任务和 PP-OCRv6](assets/migraphxsharp-0.0.1/vision-workflow.png)

图中表达的是代码的数据流，各任务图标用于说明输出类型。实际效果应以运行示例保存的图片、文本和结果文件为准。

| 独立项目 | 已实现任务 | 可以查看的输出 |
| --- | --- | --- |
| `samples/Yolo26` | 目标检测、图像分类、实例分割、人体姿态、旋转框检测 | 框与分数、Top-5、实例 mask、关键点、旋转四边形，以及 JSON/PNG/SVG |
| `samples/PaddleOcrV6` | PP-OCRv6 tiny/small/medium 整图文本检测识别、仅识别模式 | 文字、文本框、置信度、文本行裁剪，以及 JSON/TXT/PNG/SVG |

两个项目各自包含完整代码，没有独立的公共示例项目。模型、标签、OCR 字典和输入图片由使用者准备，示例运行不依赖 Python 或 OpenCV。

以上一节克隆的 `source` 为起点，准备对应模型和图片后，可以先运行 YOLO26 检测：

```bash
dotnet run --project ./source/samples/Yolo26 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --task detect --model /models/yolo26n.onnx \
  --image /images/bus.jpg --output /results/yolo26 \
  --warmup 5 --iterations 30
```

路径替换为自己的绝对路径。`result.json` 保存结果和计时，`preview.png` 展示几何结果，`annotated.svg` 包含文字标签。示例通过 `v0.0.1` 源码的项目引用构建；前一节的 `Demo` 展示公开 NuGet 包的使用方式。

YOLO26 案例按本次使用的端到端 ONNX 导出格式处理输出，检测后处理按分数筛选，不额外执行一遍 NMS。OCR 案例包括文本检测、透视裁剪和 CTC（Connectionist Temporal Classification）识别解码，识别模型必须配套正确的字典。

两个案例的模型布局、预后处理和完整参数，会在后续文章中分别展开。现在可以直接阅读以下项目说明：

```text
https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/samples/Yolo26/README.md
https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/samples/PaddleOcrV6/README.md
```

## 六、真实 GPU 结果与速度实测

### 6.1 先确认输出，再讨论速度

发布前，我在 gfx1100 环境运行了六个 YOLO26 模型、三组 PP-OCRv6 模型和 OCR 仅识别模式，并把 C# 保存的实际输入交给独立 CPU 引擎对照。

指定公交车图片上，YOLO26 n/s 检测各得到 5 个目标，实例分割得到 4 个实例，姿态模型得到 4 个人；分类 Top-1 为 `minibus`。指定航拍图片得到 12 个飞机旋转框。三种 OCR 规格在同一张产品说明图片上均识别出 16 行文字。

CPU 对照使用 ONNX Runtime 1.24.4 CPUExecutionProvider。分割的 4 个实例二值 mask 的 IoU（交并比）均为 1；三种 OCR 共 48 条文本行的识别张量和 CTC 解码也完成核对。这证明的是指定输入上的实现结果一致，单图演示不能替代数据集准确率评估。

### 6.2 同时给出推理耗时和应用流水线耗时

![YOLO26 和 PP-OCRv6 实测延迟，分别比较推理平均耗时与包含 C# 预后处理的流水线平均耗时](assets/migraphxsharp-0.0.1/latency.png)

测试使用同一图片、batch=1、float32，每组预热 5 次后顺序测量 30 次。环境为 Ubuntu 24.04、ROCm 7.2.1、MIGraphX 2.15.0、gfx1100，CPU 配额 16 核，未锁定 GPU 时钟或调整功耗。

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

两个计时范围分别是：

- **推理：** 同步 `Program.Run` 加主机输出复制/读取。OCR 包含检测与全部 16 行识别，检测输入为 512×512，每行识别输入为 48×320。
- **流水线：** 进一步包含 C# 图像预处理、输入参数创建和后处理；OCR 包含透视裁剪，分割包含原图尺寸 mask 计算。不含图片文件读取/解码、模型解析/编译、绘制和写盘。

例如 YOLO26 n 检测的推理平均为 **3.23 ms**，对应流水线平均为 **16.82 ms**；实例分割为 **7.88 ms / 59.05 ms**。对于应用开发，这种差距很有参考价值：优化模型之外，图像处理、参数创建和后处理也会影响一次请求的耗时。

P95 使用 nearest-rank，即对样本排序后取对应百分位位置。上述数字描述指定输入与顺序测量口径，不能直接换算为视频服务或并发吞吐承诺，也没有提供与其他 GPU 引擎的性能排名。

数据来自发布前的计时源码，后续独立项目整理保持了计时算法。公开 `0.0.1` 包另完成 NuGet 还原、托管消费与程序集内容核对，包安装检查和 GPU 实测各有对应范围。实测源码地址：

```text
https://github.com/guojin-yan/MIGraphX-CSharp-API/tree/b3024abef13c6d9975602821b88fb12ce0a384b2/samples
```

## 七、适用场景、当前边界与后续方向

### 7.1 哪些项目可以先用起来

如果你正在 Linux AMD GPU 环境中部署 C# 推理服务，或者希望把已有 ONNX 模型接入 .NET 视觉应用，可以先从核心包和两个案例开始。需要与现有 HIP Stream、设备内存协作时，再评估可选适配包。

除常规 ONNX 工作流外，项目还提供 program 管理的图与 module、类型化算子属性，以及 TensorFlow 解析和量化等高级接口。它们的具体条件见使用文档，接口存在并不代表任意模型组合都已经完成运行验证。

### 7.2 首发版本需要说明的边界

- **原生环境：** 当前实测覆盖上述 Linux/ROCm/gfx1100 组合，其他设备和版本需要重新验证；NuGet 包不包含 AMD 驱动和原生运行库。
- **HIP 依赖组合：** 适配包对公开 HipSharp `0.10.0` 完成包引用编译和托管消费检查；此前 GPU 检查采用当时冻结的依赖，新的公开包组合尚未重跑完整 GPU 矩阵。
- **姿态模型：** 本次使用的模型在 MIGraphX 2.15.0 下遇到 reduce fusion 的 GPU kernel 编译问题，需要在进程启动前设置 `MIGRAPHX_DISABLE_REDUCE_FUSION=1`。
- **OCR 范围：** 当前采用矩形 unclip，未覆盖完整多边形外扩、页面方向分类、文档去畸变、表格结构和版面分析。CPU/GPU 一致也不代表文字已经逐字正确。
- **高级接口：** 常用整数、浮点数、Boolean、字符串、null 和数组属性已有类型化入口；任意 C 格式化可变参数与缺少原生销毁契约的独立 module owner 未纳入公开封装。实验性自定义算子的 GPU 回调和完整 provider 生命周期仍需进一步验证。

后续文章会依次介绍环境与首次推理、两套视觉案例、性能测量，以及动态输入/缓存和 HIP 集成。项目会继续围绕真实模型和设备反馈扩大可用范围。

## 八、项目入口与使用资料

**MIGraphXSharp 0.0.1 已经提供从模型接入到视觉结果的 C# 路径。** 对使用 AMD GPU 的 .NET 项目，可以先用最小模型确认环境，再选择接近业务的完整案例，最后用自己的数据测量兼容性与耗时。

为方便复制到浏览器，以下地址均按代码保留。

| 资源 | 地址 |
| --- | --- |
| GitHub 仓库 | `https://github.com/guojin-yan/MIGraphX-CSharp-API` |
| 核心 NuGet 0.0.1 | `https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/0.0.1` |
| HIP 适配 NuGet 0.0.1 | `https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop/0.0.1` |
| Release、源码与离线文档 | `https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v0.0.1` |
| 使用文档 | `https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/index.md` |
| 托管对象与资源管理 | `https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/guides/managed-objects.md` |
| 动态输入与模型缓存 | `https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/guides/dynamic-shapes-cache.md` |
| HIP 集成 | `https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/guides/hip-interop.md` |
| 部署与排错 | `https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/v0.0.1/docs/guides/runtime-deployment.md` |
| 系列博客 | `https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/main/docs/blog/README.md` |
| 问题反馈 | `https://github.com/guojin-yan/MIGraphX-CSharp-API/issues` |

如果运行中遇到问题，欢迎附上包版本、系统、GPU 架构、ROCm/MIGraphX 版本和最小复现。也欢迎把自己的 AMD 设备与模型结果反馈到仓库，让这条 .NET 推理路径覆盖更多实际场景。

## 文章说明

项目源码采用 Apache-2.0，AMD 运行库、ImageSharp、模型、字典和图片分别适用各自许可证。模型与演示原图由使用者另行准备。

本文编写使用了 AI 辅助，配图为本篇原创排版的海报、技术示意图与实测数据图，未生成或替代真实运行截图。代码、版本与性能数字依据项目源码及已有运行记录核对；应用部署仍需结合自己的设备与输入验证。

Copyright 2026 Guojin Yan.
