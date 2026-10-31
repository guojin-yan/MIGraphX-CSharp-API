# PP-OCRv6 文本检测与识别

本示例将 PP-OCRv6 ONNX 检测和识别模型组合成完整图片 OCR：BGR 预处理 → DB 概率图 → 文本四边形 → 透视裁剪 → 识别 → CTC 去重/去 blank → 文本和可视化。tiny/small/medium 使用相同入口，**识别模型和字典必须配套**。

```bash
dotnet run --project samples/PaddleOcrV6 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --det-model /models/PP-OCRv6_tiny_det_inference.onnx \
  --rec-model /models/PP-OCRv6_tiny_rec_inference.onnx \
  --dictionary /models/PP-OCRv6_tiny_rec_dict.txt \
  --image /images/document.jpg --output /results/ocr-v6-tiny \
  --expect-min-lines 1 --dump-tensors true
```

省略 `--det-model` 时，将输入视作单张文本行，仅执行识别。默认识别输入 `[1,3,48,320]`，可用 `--rec-width` 为长文本选择更宽的静态输入；过长文本在给定宽度内压缩，不声称具备自动长行切片能力。检测按 `--det-limit`（默认 960）限制长边，宽高取 32 的倍数；通过 ONNX 静态 shape override 一次编译。没有对多种输入尺寸逐张重新编译。

检测 BGR 通道按配置 `(value/255 - mean)/std` 归一化，mean=`[0.485,0.456,0.406]`，std=`[0.229,0.224,0.225]`。DB 默认像素阈值 0.2、框分数 0.4、unclip ratio 1.4；C# 使用 8 邻域轮廓、凸包最小面积矩形与矩形外扩，不依赖 Python/OpenCV。与 Paddle 官方多边形 offset 的任意轮廓结果可能有差异。

识别 BGR `/255` 后按 mean/std=`0.5` 归一化，保持比例 resize、右侧在归一化后补 0。字典前加 CTC blank、后加空格，并严格核对输出类别数。保留被 blank 分隔的重复字符，仅去除相邻同一类重复。`--text-threshold` 默认 0.5。

读图时先按 EXIF 标记统一像素方向，再检测和裁剪，输出坐标对应统一方向后的图片。这不会判断图片内容是否倒置；没有 EXIF 标记的倒置文档仍需使用者先校正方向。

输出包括 `text.txt`、`result.json`、`preview.png`、带中文文字的独立 `annotated.svg` 和每个 `crop-xxx.png`。`--dump-tensors true` 保存 detector 与逐行 recognizer 实际输入/输出，便于独立 CPU reference 检查。输出目录请放在源码仓库外。

这是图片文本检测/识别示例，不包含页面方向分类、文档去畸变、表格结构或版面分析；竖长文本裁剪会旋转 90°。识别到的文字仍需按业务规则校验，不把单张演示等同于数据集准确率。模型及字典由使用者提供；模型、ImageSharp 和本仓库各自许可独立适用。
