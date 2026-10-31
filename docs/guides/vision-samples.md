# YOLO26 与 PP-OCRv6 图片推理示例

`samples/Yolo26` 提供检测、分类、实例分割、人体姿态和旋转框五个任务。`samples/PaddleOcrV6` 提供图片文字检测与识别，也支持只识别已经裁剪的文本行。它们直接使用本库的 ONNX parse、GPU compile、typed argument 和 host output API，图像预处理及任务后处理均为 C#。

先按[部署指南](runtime-deployment.md)安装匹配的 ROCm/MIGraphX，准备本地 ONNX 模型、原图和配套标签/字典。示例需要 .NET 10，库本身仍保持原有 15 TFM；这些模型不随库包分发。

```bash
dotnet run --project samples/Yolo26 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 --task detect \
  --model /models/yolo26n.onnx --image /images/bus.jpg --output /results/detect

dotnet run --project samples/PaddleOcrV6 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --det-model /models/PP-OCRv6_tiny_det_inference.onnx \
  --rec-model /models/PP-OCRv6_tiny_rec_inference.onnx \
  --dictionary /models/PP-OCRv6_tiny_rec_dict.txt \
  --image /images/document.jpg --output /results/ocr
```

仓库根执行上述命令。YOLO 的 `--task` 可换为 `classify`、`segment`、`pose`、`obb`，同时更换对应模型。默认输入边长分别为 224、640、640、1024，检测也是 640。YOLO 必须是示例支持的端到端输出布局；类别标签用 `--labels` 传入按索引排列的 UTF-8 文件。OCR 的识别字典必须与模型版本/大小配套。

两种示例都输出 JSON、PNG 几何预览和独立 SVG 标签图；OCR 另输出文本与裁剪，分割另输出每实例 mask。所有输出放在源码仓库外。增加 `--dump-tensors true` 可保留实际 float32 输入/输出，方便把完全相同的预处理结果交给其他引擎复核。

输入图片会先按 EXIF 标记统一方向。ROCm 7.2.1 / MIGraphX 2.15.0 的 gfx1100 上，所用姿态模型需要在启动前设置 `MIGRAPHX_DISABLE_REDUCE_FUSION=1`，详见 YOLO26 示例 README 的版本说明。

推理耗时包含同步执行和主机输出复制，解析、编译单列；不能当作纯 GPU 时间或不同硬件上的性能承诺。模型相关性、数值误差和后处理应以实际应用数据校验；当前支持范围仍以[平台说明](../compatibility/platforms.md)为准。更完整的参数、预处理和输出说明位于各示例目录的 README。
