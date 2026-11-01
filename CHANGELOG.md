# Release notes / 发行说明

## 0.0.2 — 2026-11-01

新增显存输出与完整 HIP 视觉流水线。0.0.1 保留为初始发行版，本次核心包和 HIP 适配包均升级为 0.0.2，HipSharp 依赖仍为公开的 0.10.0。

- `RunDeviceIntoAsync`：按输出形状接受调用方显存缓冲区，在同一 stream 上 D2D 复制模型输出，可直接提交 GPU 后处理，无需完整张量回传。输入、输出、program 和原生结果保活至执行完成；支持部分复制提交失败后的安全清理。
- YOLO26 五任务：HIP resize/归一化/布局转换、Top-5、检测框/旋转框/关键点解码、实例 mask 生成和计数。普通推理只回传紧凑结果，mask 保存时才回传。
- PP-OCRv6：HIP 检测预处理、8 邻域连通域、分块前缀扫描、凸包/最小面积矩形、框排序、透视裁剪、识别预处理和 CTC。CPU 负责少量调度数据、最终 Unicode 映射和图片文件读写。
- OCR 识别支持 `--rec-batch` 和 `--rec-workers`：独立 program、非阻塞 stream 与可写缓冲区，按批次提交文本行；末批补齐、只解码有效行，保持原始行顺序。整图默认 batch=4、2 个识别实例；仅识别单行默认 1/1。
- 两个示例默认 `--backend hip`，需指定对应设备的 `--gpu-architecture`（HIPRTC）或 `--hip-module`（预编译模块）；`--backend cpu` 保留原 C# 前后处理。两种后端均由 MIGraphX 执行 GPU 模型推理。
- HIP 阶段用 event 计时，模型阶段包含 D2D 复制；整体 pipeline 用墙钟计时，包含上传、前后处理和紧凑结果回传，排除文件解码、编译、绘制及产物导出。不能将旧的“推理+主机复制”分项当作相同口径。

原生基线仍为 Ubuntu 24.04、ROCm 7.2.1、MIGraphX 2.15.0、gfx1100。设备输出、同流后处理及提前释放已使用公开 HipSharp 0.10.0 执行；全部视觉任务已完成 GPU 运行。预处理浮点/字节舍入与 ImageSharp CPU 路径可有细小差异，不承诺逐元素一致。此接口使用 D2D 复制，不是零复制；实验性回调及其他平台边界仍见[平台说明](docs/compatibility/platforms.md)。

The new device-output API keeps tensors on the GPU through ordered D2D copies into leased caller buffers. The self-contained YOLO26 and PP-OCRv6 samples now default to HIP preprocessing/postprocessing and retain the CPU backend for comparison. Existing host-output APIs keep their behavior.

## 0.0.1 — 2026-10-31

首个 GitHub / NuGet 版本，同时提供可直接作为本地 NuGet 源使用的离线包。核心功能和图片示例已完成开发；实验性功能继续按使用文档中的边界提供。

### 功能

- 核心包 `JYPPX.ROCm.MIGraphX.CSharp.API`：原生库诊断、资源所有权、形状与类型化输入、ONNX 解析/编译/同步运行、动态维度、持久化与模型缓存、类型化 operation 属性及图 API。
- 可选包 `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop`：HIP 流提交、完成时输出复制及受管理的设备输入；精确依赖同版本核心包与 HipSharp `0.10.0`。
- 两个包均包含 `net46` 至 `net10.0` 的 15 个目标框架程序集及中英文 XML API 文档。
- `samples/Yolo26`：检测、分类、实例分割、姿态和旋转框；`samples/PaddleOcrV6`：tiny/small/medium 文本检测与识别，以及仅识别模式。两个示例各自包含完整代码，无公共示例项目依赖。
- 示例支持 EXIF 方向处理、JSON/PNG/SVG 输出、张量导出和预热后重复计时，模型、字典和输入图片由使用者另行准备。

### 运行范围与限制

固定原生基线为 Ubuntu 24.04 x86-64、ROCm 7.2.1、MIGraphX `2.15.0.70201-81~24.04`、gfx1100。包只包含托管程序集，需单独安装 AMD 系统原生库；Windows/macOS 托管编译不表示具有官方原生推理支持。

发布前已执行官方运行时功能检查，并对六个 YOLO26 模型、三种 OCR 规格及仅识别模式做真实 GPU 运行和独立 CPU 张量对照。高级 API、任意新模型、其他设备与版本仍需应用验证。custom-op GPU callback、完整生命周期、长期压力、零拷贝、并行重叠及任意工作负载的性能均不作全面支持保证。

- 姿态模型在上述 MIGraphX 2.15.0 环境需要显式设置 `MIGRAPHX_DISABLE_REDUCE_FUSION=1`。
- OCR 使用矩形 unclip；不等同于 Paddle 的完整多边形 offset，不包含页面方向分类、文档去畸变、表格或版面分析。
- 通用 C 可变参数与无原生 destroy/ownership 契约的独立 `migraphx_module_create` owner 不在公开封装范围；使用类型化属性和 program-owned module views。
- 示例为静态尺寸、batch=1、float32 图片推理，不包含训练、视频跟踪或摄像头服务。

安装见[入门指南](docs/guides/getting-started.md)，功能边界见[平台说明](docs/compatibility/platforms.md)，示例使用见[图片推理指南](docs/guides/vision-samples.md)。本次发布核心包和 HIP 适配包 `0.0.1`；使用已公开的 HipSharp `0.10.0`，不重新发布该依赖。公开的 HipSharp `0.9.1` 缺少适配器需要的内部流租约接口，不能用于此适配包。

The first GitHub release provides managed core and optional HIP adapter packages, 15 target frameworks, bilingual API XML documentation, and two self-contained vision sample projects. Native ROCm/MIGraphX must be installed separately. Experimental features and platform limits remain documented; this release does not qualify every model, GPU, native version or callback lifetime.
