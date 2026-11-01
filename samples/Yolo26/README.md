# YOLO26 五任务示例

默认使用 HIP 完成图像预处理、输出解码和实例 mask 计算，MIGraphX 执行 GPU 推理。支持 Ultralytics YOLO26 **端到端 ONNX 导出**，不会对端到端输出再执行一遍 NMS。`--backend cpu` 可选择 C# 前后处理，便于对照。

这是独立示例项目，图像、推理、HIP kernel、解码和计时源文件都在本目录，引用本库的 HIP 适配器和 ImageSharp。无需引用其他示例项目。

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
  --backend hip --gpu-architecture gfx1100 \
  --task detect --model /models/yolo26n.onnx --image /images/bus.jpg \
  --labels /models/coco-labels.txt --output /results/yolo26-detect \
  --expect-min-results 1 --dump-tensors true
```

其他任务更换 `--task`、对应模型与标签即可。`--size` 可以显式指定输入边长，但必须与导出模型一致。`--threshold` 默认 0.25，`--max-detections` 默认 100。`--labels` 为 UTF-8 文件，每行一个类别，顺序必须与模型匹配；省略时使用 `class-数字`。

`--gpu-architecture` 必须与设备匹配，示例通过 HIPRTC 在初始化时编译内嵌 kernel。也可以用 `--hip-module /absolute/path/kernels.hsaco` 加载针对该 GPU 预编译的模块，此时无需 HIPRTC。`--hip-native`、`--hiprtc-native` 可指定原生库，`--device` 默认 0；设备必须与 MIGraphX GPU target 一致。初始化失败会明确报错。

图片解码后上传 RGB 一次；resize、letterbox、通道/布局转换、归一化、坐标恢复、Top-5、关键点和 mask 均在显存完成。`RunDeviceIntoAsync` 在同一 stream 将模型输出 D2D 复制到复用缓冲区，再启动后处理；这条路径没有完整张量 D2H，也不属于零复制。CPU 只负责调度、类别名称和结果展示。分割 mask 在保存 PNG 时才回传。文件解码、绘图和编码仍由 ImageSharp 完成。

ROCm 7.2.1 / MIGraphX 2.15.0 的 gfx1100 测试中，所用姿态 ONNX 在默认 reduce fusion 下触发 GPU kernel 编译错误。该环境运行 `pose` 前需要在进程启动前设置 `export MIGRAPHX_DISABLE_REDUCE_FUSION=1`；示例不会自动重试或修改模型。升级 provider 后请重新核对是否仍需此设置。

输出包括 `result.json`、`preview.png` 和独立可打开的 `annotated.svg`；分割另存每个实例的 `mask-xxx.png`。PNG 显示几何/掩码，SVG 额外包含类别及分数。输出应位于源码仓库外。`--dump-tensors true` 保存实际预处理输入、原始 float32 输出和形状，便于用另一推理引擎对同一输入复核。

检测类任务使用 RGB `/255`、保留宽高比缩放和 114 灰色 letterbox，结果按同一缩放与 padding 还原。分类使用中心方形裁剪、bicubic resize 与 RGB `/255`。分割将 mask coefficients 与 prototype 相乘，在 prototype 空间裁剪，再双线性上采样、去除 padding，logit 大于 0 即前景。姿态仅绘制置信度不低于 0.5 的关键点/连接。

增加 `--warmup 5 --iterations 30` 可执行 5 次预热和 30 次正式测量。默认 0 次预热、1 次测量，只适合演示。HIP 的 `performance.pipeline` 是已解码 RGB 上传到最终紧凑结果回传的墙钟时间，包含完整前后处理。`uploadGpu`、`preprocessGpu`、`inferenceGpuWithDeviceCopies`、`postprocessGpu` 使用同一 stream 的 HIP event；推理阶段包含 D2D 输出复制。GPU 阶段之和不包含所有主机调度开销。文件解码、模型/kernel 编译、绘制、完整张量/mask 导出和写盘均不计时。

`--backend cpu` 的 pipeline 包含 C# 前后处理、argument/map 创建、同步推理与完整输出回传，`stages.inference` 是 `Program.Run` 加主机读取。两种 backend 的推理阶段口径不同；比较整条流程请使用 pipeline。两种 resize 的浮点运算与字节舍入可能略有差异，不能据此承诺逐元素一致。

JSON 保存全部原始样本、平均/P50/P95/min/max、首次执行、预热次数和测量次数。百分位使用 nearest-rank；`imagesPerSecond=1000/pipeline.mean` 是当前单进程顺序处理、同一张图片上的吞吐换算。示例为 batch=1、float32、静态尺寸，不包含摄像头、视频跟踪或训练。

依赖 ImageSharp 3.1.12 处理图像，许可证见 [Six Labors](https://sixlabors.com/license/)。模型、标签与演示原图由使用者提供；Ultralytics 模型适用其自身许可，未随本库重新分发。

ImageSharp 3.1.12 有公开安全公告，本示例面向可信本地图片；不要直接用它接收不可信图片上传。升级新版须按 Six Labors 的许可证配置。两个已发布库包均不依赖 ImageSharp。
