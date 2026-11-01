# YOLO26 与 PaddleOCR v4 / v5 / v6 图片推理示例

`samples/Yolo26` 提供检测、分类、实例分割、人体姿态和旋转框五个任务。`samples/PaddleOcr` 在同一个项目中提供 PP-OCRv4/v5 的 mobile、server 与 PP-OCRv6 的 tiny、small、medium 图片文字检测与识别，也支持只识别已经裁剪的文本行。默认使用 HIP 完成前后处理，并用本库的 `RunDeviceIntoAsync` 保持模型输出在显存；`--backend cpu` 保留 C# 前后处理作为对照。

两个示例各自包含完整实现，仅直接引用本库和 ImageSharp，没有独立的公共示例项目。

先按[部署指南](runtime-deployment.md)安装匹配的 ROCm/MIGraphX，准备本地 ONNX 模型、原图和配套标签/字典。示例需要 .NET 10，库本身仍保持原有 15 TFM；这些模型不随库包分发。

```bash
dotnet run --project samples/Yolo26 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 --task detect \
  --backend hip --gpu-architecture gfx1100 \
  --model /models/yolo26n.onnx --image /images/bus.jpg --output /results/detect

dotnet run --project samples/PaddleOcr -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --backend hip --gpu-architecture gfx1100 \
  --ocr-version v5 --variant mobile --model-dir /models/PP-OCRv5 \
  --image /images/document.jpg --output /results/ocr
```

仓库根执行上述命令。YOLO 的 `--task` 可换为 `classify`、`segment`、`pose`、`obb`，同时更换对应模型。默认输入边长分别为 224、640、640、1024，检测也是 640。YOLO 必须是示例支持的端到端输出布局；类别标签用 `--labels` 传入按索引排列的 UTF-8 文件。OCR 的识别字典必须与模型版本/大小配套。

架构参数须与 GPU 对应，HIPRTC 仅在初始化时编译内嵌 kernel。也可用 `--hip-module` 加载预编译模块。HIP 路径从解码后的 RGB 图片上传开始，在 GPU 执行 resize、布局转换、归一化、模型推理和数值后处理；模型输出在同一 stream 上 D2D 复制到复用缓冲区。CPU 处理少量调度数据、最终标签/Unicode、文件和绘图。分割 mask、OCR 概率图及识别张量不因普通推理回传，仅导出时回传。

OCR 用 `--ocr-version v4|v5|v6` 和对应 `--variant` 选择模型，`--model-dir` 指向版本目录；识别字典随所选规格匹配。v6 的版本目录中保留 tiny/small/medium 子目录。增加 `--recognition-only true` 可只识别文本行。显式模型与字典路径仍可使用，详见 [PaddleOCR 示例](https://github.com/guojin-yan/MIGraphX-CSharp-API/tree/main/samples/PaddleOcr)。统一入口和 v4/v5 支持位于 main 源码，v0.0.2 发布标签仍保留原 v6 示例路径。

两种示例都输出 JSON、PNG 几何预览和独立 SVG 标签图；OCR 另输出文本与裁剪，分割另输出每实例 mask。所有输出放在源码仓库外。增加 `--dump-tensors true` 可保留实际 float32 输入/输出，方便把完全相同的预处理结果交给其他引擎复核。

OCR 整图 HIP 路径默认以 4 行为一个识别 batch、使用 2 个独立识别实例；仅识别单行时默认 batch=1、实例数=1。可用 `--rec-batch`（1–32）和 `--rec-workers`（1–4）调整。每个实例有独立 program、非阻塞 stream 和可写缓冲区，各实例先提交再等待；末批补 0，结果保持原始行顺序。模型须支持所选 batch；更多实例会增加显存和编译成本，并不保证每种 GPU 都更快。可与 `--rec-batch 1 --rec-workers 1` 基线比较 pipeline。跨 stream 的 GPU 分项合计存在重叠，应以整图墙钟时间衡量加速。

输入图片会先按 EXIF 标记统一方向。ROCm 7.2.1 / MIGraphX 2.15.0 的 gfx1100 上，所用姿态模型需要在启动前设置 `MIGRAPHX_DISABLE_REDUCE_FUSION=1`，详见 YOLO26 示例 README 的版本说明。

速度测试使用 `--warmup 5 --iterations 30`，JSON 将保留原始样本和平均/P50/P95。`pipeline` 包含从已解码图片到最终紧凑结果的上传、预处理、推理与后处理；OCR 包含检测、裁剪和全部文本行识别，分割包含 mask 计算。文件解码、模型/kernel 编译、绘制、完整张量/mask 导出与写盘排除。HIP 推理分项使用 event，包含 D2D 输出复制；CPU backend 推理分项是同步执行加主机输出读取。比较整体速度应使用 pipeline，不能直接比较不同口径的推理分项。默认无预热、仅一次测量。

模型相关性、数值误差、后处理和速度应以实际应用数据校验；当前支持范围仍以[平台说明](../compatibility/platforms.md)为准。更完整的参数、预处理和输出说明位于各示例目录的 README。

## CLIP 与 PP-Structure

main 另提供两个独立 .NET 10 项目：[CLIP](../../samples/Clip/README.md) 和 [PP-Structure](../../samples/PPStructure/README.md)。它们都自带完整代码、下载脚本、输入清单及计时输出。

CLIP 使用 ViT-B/32 GPU 图像/文本编码器，提供英文 BPE、图片 batch、图到文匹配和文到图检索；图像处理与特征排序在 CPU。PP-Structure 使用 GPU 方向/版面/表格模型及 HIP OCR，SLANeXt 由原始图拆成 encoder/step 后在 C# 调度。公式必须显式选择 `--formula-backend ort-cpu`，否则区域保留并标为待复核；FormulaNet 原始动态循环当前无法由 MIGraphX 2.15 执行。

这两个项目不在 v0.0.2 标签内，不能把新增 sample 能力视为该版本发布内容。文档示例中的 Python 脚本只负责一次性 ONNX 准备；核心库没有新增图像、分词或 ORT 依赖。
