# OpenCV 图像迁移与部署 / OpenCV imaging

当前四个视觉示例使用 [guojin-yan/OpenCV-CSharp-API](https://github.com/guojin-yan/OpenCV-CSharp-API) 发布的 `JYPPX.OpenCV.CSharp.API 5.0.0`，配套同版本的 mini 原生运行包。核心 MIGraphX 包和 HIP 适配包不依赖图像库。

All four vision samples use the published OpenCV CSharp API 5.0.0 and a matching mini native runtime. Neither inference library package gains an image dependency. The shared `samples/Imaging/OpenCvImage.cs` is source-linked into each sample's own namespace; samples remain independent assemblies.

## 运行包 / Runtime selection

Windows x64 默认引用 `JYPPX.OpenCV.runtime.win-x64.mini`；Ubuntu 24.04 x64 默认引用 `JYPPX.OpenCV.runtime.ubuntu.24.04-x64.mini`。两者均固定为 5.0.0。NuGet 自动将原生库部署到构建输出，无需安装 Python OpenCV，也不要把其他项目的 OpenCvSharp 原生 DLL 混入输出。

Windows x64 and Ubuntu 24.04 x64 select their matching 5.0.0 mini runtime automatically. On another distribution, explicitly select a runtime published by that repository and validate it there. An Ubuntu 22.04 x64 example:

```bash
dotnet build samples/Yolo26/Yolo26.csproj -c Release \
  -p:OpenCvSampleRuntimePackage=JYPPX.OpenCV.runtime.ubuntu.22.04-x64.mini
```

其他 CPU 架构或交叉发布同样须显式选择匹配运行包并在中央包清单加入其精确版本，不可将 x64 二进制当作其他架构运行包。MIGraphX/HIP 的 GPU 支持仍由各自运行时与硬件决定；OpenCV 运行包不能扩展推理库的平台范围。

## 行为变化 / Behavioral changes

| 功能 | 当前行为 |
| --- | --- |
| 图片读取 | .NET 读取文件字节，OpenCV 解码并应用 EXIF 方向一次，支持 Unicode 文件路径 |
| 颜色顺序 | 内部保持连续 RGB8；编解码边界显式转换 BGR/RGB；HIP 上传与 NCHW 的 RGB/BGR 选择保持明确 |
| CPU 缩放 | OpenCV `Linear` / `Cubic`；对应原双线性/双三次用途，滤波与舍入不承诺逐像素相同 |
| letterbox / OCR | 保留 114 灰色、原缩放与坐标恢复；识别和表格 padding 在归一化后补零 |
| OCR 裁剪 | 保留四角投影采样、边界夹紧和竖长文本旋转规则 |
| 导出 | RGB 预览 PNG、单通道 mask PNG、保留 alpha 的 RGBA 叠加、含 UTF-8 标签的独立 SVG |
| CLIP / 文档 / 公式 | 保留各自中心裁剪、通道与归一化配置；缩放改用 OpenCV，须重新核对任务精度 |

The HIP resize kernels are unchanged. Their sampling and byte rounding can differ from OpenCV's CPU resize, particularly when downsampling. Compare model accuracy and performance using the same backend and actual inputs. Historical image tensors and benchmark figures are not a new numerical or performance baseline after this migration.

图像入口限制编码文件不超过 64 MiB，解码后每边不超过 32768 像素、总像素不超过 8000 万。5.0.0 的该解码 API 不提供托管解码前尺寸预算；尺寸检查发生在原生分配之后。这些示例不构成面向任意上传文件的隔离解码服务。

## 验证 / Verification

```powershell
./eng/build.ps1
dotnet test tests/Vision.SampleTests/Vision.SampleTests.csproj -c Release --no-build
```

回归覆盖 PNG 颜色与 HIP 字节顺序、EXIF 镜像/旋转、独立裁剪生命周期、极窄图片 letterbox、灰度与 alpha、OCR 补零、CLIP 和文档归一化及坏图片拒绝。正式模型运行继续使用各示例的命令和外部素材；图像回归通过不能代替完整数据集精度评估。

Image decoding, resizing, rotation and encoding are provided by OpenCV under its own licenses. Models, dictionaries and images retain their separate license terms.
