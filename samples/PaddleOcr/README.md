# PaddleOCR v4 / v5 / v6 文本检测与识别

本示例将 PP-OCRv4、PP-OCRv5、PP-OCRv6 ONNX 检测和识别模型组合成完整图片 OCR：BGR 预处理 → DB 概率图 → 文本四边形 → 透视裁剪 → 识别 → CTC 去重/去 blank → 文本和可视化。三代模型都放在同一个项目中，**识别模型和字典必须配套**。

这是独立示例项目，图像、推理、HIP kernel、OCR 流水线与计时源文件都在本目录，引用本库的 HIP 适配器和 ImageSharp。默认 `--backend hip`；`--backend cpu` 保留 C# 前后处理作为对照。

## 选择模型版本

| `--ocr-version` | `--variant` | 识别字典 | 识别输出类别数（含 blank、空格） |
| --- | --- | --- | ---: |
| `v4` | `mobile` / `server` | `ppocrv4_keys.txt` | 6625 |
| `v5` | `mobile` / `server` | `ppocrv5_dict.txt` | 18385 |
| `v6` | `tiny` | `PP-OCRv6_tiny_rec_dict.txt` | 6906 |
| `v6` | `small` / `medium` | 对应规格的 `PP-OCRv6_*_rec_dict.txt` | 18710 |

`--model-dir` 指向对应版本目录：v4/v5 目录内放 `PP-OCRvN_mobile_det.onnx`、`PP-OCRvN_mobile_rec.onnx` 或对应 server 文件，以及字典。v6 目录下保留 `tiny/`、`small/`、`medium/`，其中放对应 `*_inference.onnx` 和字典。版本默认 v6，规格默认 v4/v5 的 mobile 或 v6 的 tiny。

以下示例运行 v5 mobile。换成 v4 时替换版本及目录；server 只需更换 `--variant`：

```bash
dotnet run --project samples/PaddleOcr -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --backend hip --gpu-architecture gfx1100 \
  --ocr-version v5 --variant mobile --model-dir /models/PP-OCRv5 \
  --rec-batch 4 --rec-workers 2 \
  --image /images/document.jpg --output /results/ocr-v5-mobile
```

v6 同样使用这个项目：

```bash
dotnet run --project samples/PaddleOcr -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --backend hip --gpu-architecture gfx1100 \
  --ocr-version v6 --variant tiny --model-dir /models/PP-OCRv6 \
  --image /images/document.jpg --output /results/ocr-v6-tiny
```

也可以继续显式指定文件。`--det-model`、`--rec-model` 和 `--dictionary` 覆盖所选版本的默认路径；不传 `--model-dir` 时，识别模型和字典必填，检测模型可选：

```bash
dotnet run --project samples/PaddleOcr -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --backend hip --gpu-architecture gfx1100 \
  --det-model /models/PP-OCRv6_tiny_det_inference.onnx \
  --rec-model /models/PP-OCRv6_tiny_rec_inference.onnx \
  --dictionary /models/PP-OCRv6_tiny_rec_dict.txt \
  --rec-batch 4 --rec-workers 2 \
  --image /images/document.jpg --output /results/ocr-v6-tiny \
  --expect-min-lines 1 --dump-tensors true
```

使用 `--model-dir` 时会默认加载检测与识别模型；增加 `--recognition-only true` 将输入视作单张文本行，仅执行识别。只传显式文件时，省略 `--det-model` 同样执行仅识别。整图模式默认识别输入 `[4,3,48,320]`，使用 2 个独立识别实例；仅识别模式默认 batch=1、实例数=1。可用 `--rec-width` 为长文本选择更宽的静态输入；过长文本在给定宽度内压缩，不声称具备自动长行切片能力。检测按 `--det-limit`（默认 960）限制长边，宽高取 32 的倍数；通过 ONNX 静态 shape override 一次编译。没有对多种输入尺寸逐张重新编译。

HIP 识别可设置 `--rec-batch 1..32` 和 `--rec-workers 1..4`。每个实例独立持有 MIGraphX program、非阻塞 HIP stream、输入输出及裁剪缓冲区，复用已编译模型。按检测后的文本阅读顺序组 batch，先提交同一轮各个实例，再等待这一轮完成；结果按原始行号回填。最后不足一个 batch 的槽位补 0，只解码有效文本行。实例之间共享只读原图和几何信息，禁止并发复用同一 program 的可写缓冲区。模型须支持所选 batch，示例不在运行中重新导出 ONNX，也不会失败后悄悄退回 batch=1。

多 batch 和多实例会增加编译时间及显存占用，是否更快取决于模型、文本行数和 GPU。可用 `--rec-batch 1 --rec-workers 1` 测单实例基线，再与 batch=4、实例数=1/2 比较整图 pipeline。文本很少时可减小 batch 或实例数。`--backend cpu` 仅用于原来的单行 CPU 前后处理对照，不接受这两个调度参数。

`--gpu-architecture` 必须对应 GPU，HIPRTC 在初始化时编译内嵌 kernel；也可用 `--hip-module /absolute/path/kernels.hsaco` 加载预编译模块。可通过 `--hip-native`、`--hiprtc-native` 指定原生库。`--device` 默认 0，须与 MIGraphX target 一致。

HIP 路径在显存中完成检测 resize/归一化、DB 阈值/连通域/凸包/最小面积矩形、框排序、透视裁剪、识别 resize/补边和 CTC 解码。模型输出通过同一 stream 的 D2D 复制进入复用缓冲区。CPU 读取少量计数和裁剪尺寸来调度 kernel，最后回传四边形、字符 ID 与置信度并映射为 Unicode。图片解码、绘图和文件编码在 CPU；完整概率图和识别张量仅在显式导出时回传。

检测 BGR 通道按配置 `(value/255 - mean)/std` 归一化，mean=`[0.485,0.456,0.406]`，std=`[0.229,0.224,0.225]`。DB 默认像素阈值 0.2、框分数 0.4、unclip ratio 1.4；两种 backend 使用 8 邻域轮廓、凸包最小面积矩形与矩形外扩，不依赖 Python/OpenCV。HIP 用分块前缀扫描生成稳定的连通域编号与轮廓点偏移，避免单线程扫描整幅概率图。与 Paddle 官方多边形 offset 的任意轮廓结果可能有差异。HIP 最多处理 3000 个连通域，超出明确报错；裁剪边长上限 8192。

识别 BGR `/255` 后按 mean/std=`0.5` 归一化，保持比例 resize、右侧在归一化后补 0。字典前加 CTC blank、后加空格，并严格核对输出类别数。保留被 blank 分隔的重复字符，仅去除相邻同一类重复。`--text-threshold` 默认 0.5。

读图时先按 EXIF 标记统一像素方向，再检测和裁剪，输出坐标对应统一方向后的图片。这不会判断图片内容是否倒置；没有 EXIF 标记的倒置文档仍需使用者先校正方向。

输出包括 `text.txt`、`result.json`、`preview.png`、带中文文字的独立 `annotated.svg` 和每个 `crop-xxx.png`。`--dump-tensors true` 保存 detector 与逐行 recognizer 实际输入/输出；批处理输出按 batch 槽位拆成单行张量，并记录 `executionBatchSize`、`batchIndex`，便于独立 CPU reference 检查。输出目录请放在源码仓库外。

增加 `--warmup 5 --iterations 30` 可预热 5 张、测量 30 张，每次都完整执行检测、DB 解码、透视裁剪、识别预处理与 CTC 解码。默认只测量 1 次、不预热。`performance.pipeline` 是从已解码图片上传到最终文字的墙钟时间，排除文件解码、模型/kernel 编译、绘制和产物导出。HIP 的各个 `*Gpu*` 阶段用 HIP event 计时，模型阶段包含 D2D 输出复制；CPU backend 的 `detector`、`recognitionLine`、`recognitionTotal`、`inferenceTotal` 是同步推理加完整主机输出读取。推理阶段口径不同，比较整体速度请使用 pipeline。逐行数据不能当作整图速度；仅识别模式的 `imagesPerSecond` 对应输入文本行。

JSON 记录 `ocrVersion`、`variant`、原始样本、平均/P50/P95/min/max（nearest-rank 百分位）、首次执行、预热和测量次数，以及识别 batch 和实例数。`recognitionBatchGpuWithDeviceCopies` 记录每批模型执行加 D2D 复制；不同 stream 的分项合计可能重叠，不能当作整图墙钟时间。`imagesPerSecond=1000/pipeline.mean` 是同一张图片、单进程顺序处理的换算。计时后另外执行一次相同流程保存裁剪和张量，避免写盘影响计时，并核对保存文字与测量结果一致。

这是图片文本检测/识别示例，不包含页面方向分类、文档去畸变、表格结构或版面分析；竖长文本裁剪会旋转 90°。识别到的文字仍需按业务规则校验，不把单张演示等同于数据集准确率。模型及字典由使用者提供；模型、ImageSharp 和本仓库各自许可独立适用。

图像依赖 ImageSharp 3.1.12 有公开安全公告，本示例面向可信本地图片；不要直接用于不可信图片上传。升级新版须按 Six Labors 的许可证配置。两个已发布库包均不依赖 ImageSharp。

## batch 与实例数实测

PP-OCRv6 tiny，同一张 512×512 图片、16 行文字，gfx1100 / ROCm 7.2.1 / MIGraphX 2.15.0 / float32，5 次预热、30 次测量，未锁频。下表全部使用 HIP 前后处理，记录包含检测、裁剪和所有文本行识别的整图墙钟时间，编译和导出排除。

| 识别 batch | 识别实例数 | Pipeline 平均 ms | Pipeline P95 ms |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 17.77 | 18.99 |
| 4 | 1 | 11.50 | 11.63 |
| 4 | 2 | 10.97 | 11.08 |

batch=3 的末批仅有 1 行、单行输入和空白图也已核对；补齐槽位不进入结果，多种配置保持原始行顺序与文字。更多实例不保证更快，应以自己的图片和模型实测选择。
