# YOLO26 五任务示例

纯 C# 预处理与后处理，MIGraphX GPU 推理。支持本项目测试所用的 Ultralytics YOLO26 **端到端 ONNX 导出**，不把 YOLOv8 原始输出误当成 YOLO26；不会对端到端输出再执行一遍 NMS。

| `--task` | 默认输入 | 输出与演示 |
| --- | --- | --- |
| `detect` | 640×640 | `[1,N,6]`：xyxy、score、class；检测框 |
| `classify` | 224×224 | `[1,classes]` 已归一化概率；Top-5 |
| `segment` | 640×640 | `[1,N,38]` + `[1,32,160,160]`；独立实例 mask、检测框 |
| `pose` | 640×640 | `[1,N,57]`；17 个 COCO 关键点和骨架 |
| `obb` | 1024×1024 | `[1,N,7]`：cxcywh、score、class、弧度角；旋转四边形 |

从仓库根目录执行（native、模型、图片和输出使用自己的绝对路径）：

```bash
dotnet run --project samples/Yolo26 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --task detect --model /models/yolo26n.onnx --image /images/bus.jpg \
  --labels /models/coco-labels.txt --output /results/yolo26-detect \
  --expect-min-results 1 --dump-tensors true
```

其他任务更换 `--task`、对应模型与标签即可。`--size` 可以显式指定输入边长，但必须与导出模型一致。`--threshold` 默认 0.25，`--max-detections` 默认 100。`--labels` 为 UTF-8 文件，每行一个类别，顺序必须与模型匹配；省略时使用 `class-数字`。

ROCm 7.2.1 / MIGraphX 2.15.0 的 gfx1100 测试中，所用姿态 ONNX 在默认 reduce fusion 下触发 GPU kernel 编译错误。该环境运行 `pose` 前需要在进程启动前设置 `export MIGRAPHX_DISABLE_REDUCE_FUSION=1`；示例不会自动重试或修改模型。升级 provider 后请重新核对是否仍需此设置。

输出包括 `result.json`、`preview.png` 和独立可打开的 `annotated.svg`；分割另存每个实例的 `mask-xxx.png`。PNG 显示几何/掩码，SVG 额外包含类别及分数。输出应位于源码仓库外。`--dump-tensors true` 保存实际预处理输入、原始 float32 输出和形状，便于用另一推理引擎对同一输入复核。

检测类任务使用 RGB `/255`、保留宽高比缩放和 114 灰色 letterbox，结果按同一缩放与 padding 还原。分类使用中心方形裁剪、bicubic resize 与 RGB `/255`。分割将 mask coefficients 与 prototype 相乘，在 prototype 空间裁剪，再双线性上采样、去除 padding，logit 大于 0 即前景。姿态仅绘制置信度不低于 0.5 的关键点/连接。

`runWithHostCopiesMilliseconds` 包含同步 GPU 执行、主机输出复制和读取；不是纯 GPU kernel 延时。模型解析与编译时间单独记录。示例为 batch=1、float32、静态尺寸，不包含摄像头、视频跟踪或训练。

依赖 ImageSharp 3.1.12 处理图像，许可证见 [Six Labors](https://sixlabors.com/license/)。模型、标签与演示原图由使用者提供；Ultralytics 模型适用其自身许可，未随本库重新分发。
