# 用 C# 组合 PP-Structure：文档版面、OCR 与表格处理

## 前言

OCR 能把文字读出来，但一页文档往往还包含标题、图片、表格和公式。把这些内容直接拼成一段文本，后续归档和展示还是不方便。

这次我在 MIGraphXSharp 中增加了独立的 `samples/PPStructure` 项目，按页面组织方向校正、版面检测、整页 OCR 和表格处理。结果既保留文字，也保留区域类别、原始页坐标和表格结构，便于继续接文档归档或人工复核界面。

先说明执行范围：方向、版面、OCR、表格模型使用 MIGraphX GPU，OCR 的数值前后处理使用 HIP。FormulaNet 的原始动态图目前无法由 MIGraphX 2.15 解析，因此公式提供显式选择的 ORT CPU 路径。这条 CPU 路径会写进结果和计时，不把它算成 AMD GPU 公式推理。

## 一、页面经过哪些步骤

方向模型先判断 0、90、180、270 度，置信度达到 0.8 才校正。随后 PP-DocLayout-L 输出 23 类区域，包含正文、标题、表格、公式和图片等。

文字检测和识别对校正后的整页执行一次。已有文字行按中心点归入对应区域，表格再继续分类、检测单元格和识别结构。这样不会为每个正文块重复编译和运行一套 OCR。

| 阶段 | 模型 | 输出 |
| --- | --- | --- |
| 方向校正 | PP-LCNet | 角度、概率 |
| 版面检测 | PP-DocLayout-L | 类别、坐标、分数 |
| 整页 OCR | PP-OCRv5 Mobile | 文字行、置信度、多边形 |
| 表格分类 | PP-LCNet | wired / wireless |
| 单元格检测 | RT-DETR-L | 格子坐标 |
| 结构识别 | SLANeXt | 行列结构、合并单元格标签 |
| 可选公式 | FormulaNet + ORT CPU | LaTeX |

区域和文字同时保存校正页、原始方向页的坐标。查看倒置页时，业务界面可以把结果重新画回原图。

## 二、表格不能只靠检测框拼出来

单元格检测给出格子位置，结构模型给出行列标签，两者解决的问题不同。当前示例只有在格数一致、行几何清楚、结构包含 EOS 时才把 OCR 文字填入 HTML。

遇到 `rowspan`、格数不匹配或明显重叠的框，会保留结构和诊断信息。这些表格进入待复核状态，不能为了让页面“看起来完整”而按坐标随意拼接。

SLANeXt 的原始 ONNX 含有动态 Loop、Range 和数组写入。直接交给 MIGraphX 2.15 会在解析阶段失败。我将原图拆成编码器和固定形状的单步解码器，在 C# 中传递 hidden/token，遇到 EOS 停止。

两部分仍使用同一套模型参数，均由 MIGraphX GPU 执行。wired、wireless 拆分图与原始模型在 EOS 之前完成 token 和概率对照；原导出图多执行一次 EOS 后的迭代，展示时不使用那一步。当前循环仍经主机数组传递状态，尚未做到设备内存常驻。

## 三、准备模型与输入

需要 Linux x64、.NET 10、ROCm 7.2.1、MIGraphX 2.15.0 和匹配的 AMD GPU。示例位于 main 源码，核心 API 沿用 `0.0.2`；已发布的 v0.0.2 标签中没有这个新项目。

仓库：`https://github.com/guojin-yan/MIGraphX-CSharp-API`。

在仓库根目录执行下面的准备命令。Python 只用于生成兼容 ONNX，后续页面处理由 C# 程序执行。

```bash
bash samples/PPStructure/fetch-assets.sh /data/document
python3 -m venv /data/document/model-tools
/data/document/model-tools/bin/pip install onnx==1.20.1 numpy==2.2.6
/data/document/model-tools/bin/python \
  samples/PPStructure/prepare-models.py /data/document/models
dotnet build samples/PPStructure/PPStructure.csproj -c Release \
  -p:JYPPXManagedTargetFrameworks=net10.0
```

准备脚本保留原文件，输出兼容图及 hash 清单。不要直接覆盖原模型，这样后续还可以用同一份原图进行对照。

默认清单包含报刊、图文混排、论文、公式专页、费用表和倒置页。图片来自 PaddleX 官方公开示例，没有人工准确率标注。模型和图片的具体来源记录在 sample README 与下载脚本中。

## 四、运行并查看输出

```bash
dotnet run --project samples/PPStructure -c Release --no-build -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 --gpu-architecture gfx1100 \
  --models /data/document/models --manifest /data/document/documents.json \
  --output /results/document --formula-backend ort-cpu \
  --rec-batch 4 --rec-workers 2 --warmup 1 --iterations 10
```

程序会复用模型。首次需要编译多套网络，耗时可能达到数分钟；这部分单独记录，不放进后面的页面性能样本。

打开输出目录中的 `index.html` 查看版面和 OCR。每页的 `page.json` 保留模型、双坐标和计时；`document.md` 是可继续整理的 Markdown；表格对齐成功才导出填字 HTML。公式启用时另存 `.tex`，并明确记录 `ort-cpu` 后端。

如果不需要公式，可以省略 `--formula-backend`。页面仍保留公式区域，同时给出 `formula-recognition-disabled` 提示。退出码 `3` 表示有内容需要复核，退出码 `1` 才表示至少一页运行失败。

## 五、看结果时要区分流程与质量

这套示例提供页面解析的起点，不包括 PDF 渲染、多栏阅读顺序恢复、跨页表格合并或人工结果回写。文字行按中心点归入区域，重叠区域可能共享文字。公式使用 ImageSharp 预处理，采样也不能视为与官方 Pillow 逐像素一致。

使用自己的资料时，建议先检查区域框和方向，再检查文字、表格对齐和公式。页面能执行结束，只说明流程完成；内容是否正确，仍应结合原图复核。

性能统计从已解码图片开始，包含方向校正、前处理、GPU 模型、HIP OCR、表格解码与文字关联；显式启用的 CPU 公式也包含在内。文件解码、模型编译、绘图和写盘排除。比较不同方案时，需要保持这些条件一致。

完整参数、输入格式与结果字段在 `samples/PPStructure/README.md`。部署到业务系统时，可以先接入 JSON 和原图定位，再逐步加入文档分组、搜索和人工复核。
