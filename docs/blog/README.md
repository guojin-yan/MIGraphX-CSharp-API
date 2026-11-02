# MIGraphX CSharp API 技术博客

这里收录可独立阅读、用于博客发表的中文技术文章。文章注明适用版本，代码和数据按对应版本核对；日常 API 查询与部署说明见[使用文档](../index.md)。

首篇介绍库的定位、主要 API、HIP 显存流水线及四类案例的用途、结果和速度。四篇实战文章均以“MIGraphX CSharp API 项目简介”为第一章，通过直接调用库接口展开模型解析、GPU 编译、类型化张量、参数绑定、异步完成和资源复用。文章使用 `0.0.2` 核心 API 和当前 main 的示例；新增示例与发布标签的差异在正文中注明。

## 首发系列

| 顺序 | 文章 | 状态 |
| --- | --- | --- |
| 1 | [MIGraphX CSharp API 发布：让 C# 接入 AMD GPU 原生推理](2026-11-01-migraphxsharp-0.0.2.md) | 核心 0.0.2、案例 main，尚未在外部博客发表 |
| 2 | 从环境安装到第一次 ONNX 推理 | 待编写 |
| 3 | [MIGraphX CSharp API 实战：用 YOLO26 理解模型编译、张量与 HIP 显存推理](2026-11-01-yolo26-five-tasks.md) | 主机张量与设备缓冲两种调用、输出所有权、五任务后处理与实测 |
| 4 | [MIGraphX CSharp API 实战：PaddleOCR 多模型调用、Batch 张量与多实例调度](2026-11-01-paddleocr-v4-v5-v6.md) | DET/REC program、固定 Batch、字典校验、异步完成和资源隔离 |
| 5 | C# 图片推理性能实测 | 待编写 |

## 进阶系列

| 顺序 | 文章 | 状态 |
| --- | --- | --- |
| 6 | 动态输入与模型缓存 | 待编写 |
| 7 | HIP 流与设备内存集成 | 待编写 |

配图与可编辑图源放在 `assets/<文章主题>/`，正文通过相对路径引用。外部博客发表后在此补充对应链接。

## 应用案例

| 文章 | 适用范围 |
| --- | --- |
| [MIGraphX CSharp API 实战：用 PP-Structure 组织多输入模型与循环解码](2026-11-01-pp-structure-document-processing.md) | 三路参数绑定、混合类型输出、encoder/step 状态传递、多模型生命周期与逐页速度 |
| [MIGraphX CSharp API 实战：CLIP 双编码器、Int64 输入与特征复用](2026-11-01-clip-image-text-retrieval.md) | float32/int64 张量、独立 batch、输出复制、program 与特征两种复用及批量速度 |

应用案例均已完成正文与配图，尚未在外部博客发表。博客正文中的外部地址以代码呈现，便于复制到不同发布平台；素材来源和图源说明见 `assets/README.md`。
