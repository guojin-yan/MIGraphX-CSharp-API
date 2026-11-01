# 博客配图与来源

文章引用 PNG 成图；流程、原理、封面与性能图同时保留同名 SVG，便于修改文字或导出其他分辨率。图内数据与正文表格对应，性能条件以正文为准。

| 目录 | 文章与配图 |
| --- | --- |
| `migraphxsharp-0.0.2/` | 首发长幅封面 `cover-four-models.png/.svg`、四类案例总览与 API 等图源；`ocr-scheduling.png` 同时用于 OCR 实战解释 batch/worker |
| `yolo26/` | 五任务封面、设备数据流、CPU/HIP 前后处理耗时、真实预测拼图 |
| `paddleocr/` | 三代模型封面、OCR 流程、CTC 原理、七配置耗时、真实文字框与结果 |
| `pp-structure/` | 文档封面、页面流程、表格对齐、报刊/混排与费用表实际输出 |
| `clip/` | 图文检索封面、双编码器、输入约定、三张商品图实际匹配 |

## 图示与真实结果的区别

`cover`、`pipeline`、`ctc`、`input-contract`、`table-alignment` 为技术示意图，展示代码中的关系与规则，不代表真实软件界面。`performance` 根据正文实测表格绘制，不包含推测数据。

`yolo26/results.png` 使用案例真实 `preview.png` 等比缩放拼接，分类 Top-5 来自同一轮结果 JSON。检测、分割、姿态和分类使用 Ultralytics `bus.jpg`，旋转框为案例使用的航拍图；未修改预测几何。模型与原图分别适用来源方许可。

`paddleocr/results.png` 使用 PP-OCRv5 Mobile 对 500×500 商品说明图的真实输出，保留完整原图范围。右侧摘录前九行原始文字和置信度，过长行仅在配图排版中省略，正文说明了完整结果所在文件。

`pp-structure/document-results.png`、`table-results.png` 根据 PaddleX 官方公开演示图与实际模型坐标绘制。图片来源为 `https://paddle-model-ecology.bj.bcebos.com/paddlex/imgs/demo_image/`，下载文件与对应名称见 `samples/PPStructure/fetch-assets.sh`。图片权利归原来源方。

`clip/clip-results.png` 使用三张 Unsplash 商品照片与真实 CLIP 分数。照片原始地址保留在 `samples/Clip/fetch-assets.sh`；使用条件为 `https://unsplash.com/license`。模型的 MIT 许可随下载文件保留。

图片仅用于说明已运行案例，不以单图展示替代准确率评测。引用或发布时保留正文中对计时范围、执行后端与素材来源的说明。
