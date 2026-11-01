# PP-Structure 文档处理

独立 .NET 10 示例，将方向校正、23 类版面检测、PP-OCRv5、表格分类、单元格检测和 SLANeXt 结构识别组合为文档流水线，输出 JSON、Markdown、表格 HTML 和原图坐标。代码包含在本项目内，不引用其他 sample 项目。

**方向、版面、OCR、表格模型由 MIGraphX GPU 执行；OCR 前后处理使用 HIP。公式识别需要显式选择 `--formula-backend ort-cpu`，由 ONNX Runtime CPU 执行。** 原始 FormulaNet 的动态循环当前无法由 MIGraphX 2.15 解析；这不是全 GPU 的公式方案。默认关闭公式识别，保留公式区域并标为待复核。

此示例位于 main 源码，未包含在 v0.0.2 发布标签中。ONNX Runtime、ImageSharp、词表解析等依赖仅属于本示例，核心库没有增加这些依赖。

## 准备模型

Linux x64、.NET 10、ROCm 7.2.1、MIGraphX 2.15.0。下载及模型准备只需做一次，推理程序由 C# 运行。

在仓库根目录执行：

```bash
bash samples/PPStructure/fetch-assets.sh /data/document
python3 -m venv /data/document/model-tools
/data/document/model-tools/bin/pip install onnx==1.20.1 numpy==2.2.6
/data/document/model-tools/bin/python samples/PPStructure/prepare-models.py /data/document/models
dotnet build samples/PPStructure/PPStructure.csproj -c Release -p:JYPPXManagedTargetFrameworks=net10.0
```

| 模型 | 输入 | 处理位置 |
| --- | --- | --- |
| PP-LCNet 方向 / 表格分类 | `[1,3,224,224]` | MIGraphX GPU |
| PP-DocLayout-L | 图像 `[1,3,640,640]`、`im_shape/scale_factor` | MIGraphX GPU |
| PP-OCRv5 Mobile DET/REC | 动态页尺寸、识别 batch=4 | MIGraphX GPU + HIP |
| RT-DETR-L wired / wireless cell | 与版面相同的三输入合同 | MIGraphX GPU |
| SLANeXt wired / wireless | `[1,3,512,512]` 编码器 + 固定形状单步解码器 | MIGraphX GPU，C# 调度循环 |
| PP-FormulaNet_plus-S | `[1,1,384,384]` | 可选 ORT CPU |

模型来源：`https://github.com/guojin-yan/DeploySharp/releases/tag/models-paddleocr`；公式 tokenizer 来自 `https://huggingface.co/PaddlePaddle/PP-FormulaNet_plus-S`。

准备脚本保留原文件，为版面/单元格模型生成 `.migraphx.onnx`，将 scalar reshape 改写为等价 Squeeze；为 SLANeXt 生成 `.encoder.onnx/.step.onnx`，移出原导出图的动态循环和数组写入。记录保存在模型目录的 `prepared-models.json`。不要把诊断阶段产生的其他兼容图混入此目录。

## 运行

```bash
dotnet run --project samples/PPStructure -c Release --no-build -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 --gpu-architecture gfx1100 \
  --models /data/document/models --manifest /data/document/documents.json \
  --output /results/document --formula-backend ort-cpu \
  --rec-batch 4 --rec-workers 2 --warmup 1 --iterations 10
```

输出必须放在源码仓库外。初次编译多个模型可能需要数分钟；每个模型在本进程内复用，换输入尺寸时只替换当前尺寸的 OCR 会话，避免缓存不断占用显存。无公式需求时省略 `--formula-backend`，含公式区域会得到 `formula-recognition-disabled` 复核提示。

`documents.json` 是页面数组：`id` 唯一，使用小写字母、数字和短横线，最多 64 字符；`path` 相对清单目录解析；`kind` 为 `auto/text/table/formula`；`autoOrient` 默认 true。支持 1–100 页。方向分数达到 0.8 才校正。

默认演示使用六张 PaddleX 官方示例图。图片来源为 `https://paddle-model-ecology.bj.bcebos.com/paddlex/imgs/demo_image/`；报刊图原名 `pp_structure_v3_demo.png`，费用表原名 `table_recognition.jpg`，其他文件保留原名。图片权利归原来源方。

## 输出与语义

| 文件 | 内容 |
| --- | --- |
| `index.html` | 页面预览、文字、表格及公式 |
| `summary.json` | 实际后端、页状态、性能及错误 |
| `<id>/page.json` | 区域、文字、正向页/原始页双坐标、模型 hash、阶段耗时 |
| `<id>/document.md`、`text.txt` | 页面 Markdown / OCR 文本 |
| `<id>/source.png`、`preview.png`、`annotated.svg` | EXIF 处理后原始方向、校正页几何及标签 |
| `<id>/table-*.json/.html` | 结构和格子；对齐成功时才生成填字 HTML |
| `<id>/formula-*.tex` | 显式启用 ORT CPU 时生成 LaTeX |
| `<id>/error.json` | 页面失败信息；其他页面继续处理 |

`Polygon` 属于校正页，`SourcePolygon` 属于 `source.png`。版面/格子检测使用 640 画布，结果只还原一次。整页 OCR 只运行一次，按文本框中心分配给区域和单元格。重叠区域可能共享文字，这不是完整的多栏阅读顺序算法。

SLANeXt 每步执行原解码器，传递 hidden/token，遇到 EOS 停止，最多 501 步。隐状态与 encoder memory 当前通过主机数组传递，因此这条循环尚不是零拷贝。wired/wireless 拆分图与原始模型在 EOS 之前完成 token 和概率对照；原导出图会多执行一次 EOS 后的迭代，展示时忽略。

结构格数、检测格数和行几何全部一致时才填字。`rowspan`、格数不匹配、缺失 EOS 或有歧义的重叠框均保留结构与诊断，不擅自拼出完整表格。公式使用 ImageSharp 进行去白边、灰度和缩放，其采样与官方 Pillow 并非逐像素一致；LaTeX 仍需人工复核。

退出码：`0` 流程完成，`1` 至少一页失败，`3` 有需复核内容。重复运行请使用新输出目录，避免将上次产物误当作本次结果。

性能的 page mean/P50/P95 从已解码页开始，包含方向、前处理、GPU 模型、HIP OCR、结构循环、文字关联及可选 CPU 公式；排除初始化/预热、绘图和写盘。模型阶段值包含主机复制；OCR 值为整条 HIP 流水线墙钟时间，不能与纯 GPU kernel 耗时直接相加比较。
