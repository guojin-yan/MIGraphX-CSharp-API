# CLIP 图文匹配与检索

独立 .NET 10 示例，使用 MIGraphX GPU 执行 OpenAI CLIP ViT-B/32 的图像、文本编码器。输入若干英文描述和图片，同时输出图到文分类、文到图检索、512 维特征和可浏览 HTML。所有代码包含在本项目内。

此示例位于 main 源码，未包含在 v0.0.2 发布标签中。核心库使用现有 API，无需更换 NuGet 版本。

## 准备与运行

需要 Linux x64、.NET 10、ROCm 7.2.1、MIGraphX 2.15.0，以及匹配的 AMD GPU。先按 `docs/guides/runtime-deployment.md` 配置原生运行时。

在仓库根目录执行，模型、输入和输出都放到仓库外：

```bash
bash samples/Clip/fetch-assets.sh /data/clip
dotnet build samples/Clip/Clip.csproj -c Release -p:JYPPXManagedTargetFrameworks=net10.0
dotnet run --project samples/Clip -c Release --no-build -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --models /data/clip/models --manifest /data/clip/inputs.json \
  --output /results/clip --batch 4 --warmup 3 --iterations 20
```

也可直接准备这五个模型文件和许可文件：

| 文件 | 用途 |
| --- | --- |
| `clip-vit-b-32.image-encoder.onnx` | RGB 图像 → `[batch,512]` 特征 |
| `clip-vit-b-32.text-encoder.onnx` | `input_ids/attention_mask` → `[prompts,512]` 特征 |
| `clip-vit-b-32.vocab.json` | BPE 词表 |
| `clip-vit-b-32.merges.txt` | BPE 合并表 |
| `clip-vit-b-32.LICENSE` | 模型 MIT 许可 |

模型下载来源：`https://github.com/guojin-yan/DeploySharp/releases/tag/models-visual.1`。
演示照片来自 Unsplash，原始图片地址保留在下载脚本中；使用条件：`https://unsplash.com/license`。

## 输入与结果

修改 `inputs.json` 的 `prompts` 和 `images`。图片路径相对清单文件所在目录解析。要求 prompt key、image ID 各自唯一，1–64 条描述、1–1000 张图片；`--batch` 范围 1–16。尾批补零，只返回实际图片的结果。

文本编码一次，图像分批编码；程序复用已经编译的两套 program。短边缩放到 224 后居中裁剪，RGB CHW，使用 CLIP 均值/方差。BPE 长度固定 77，保留 BOS/EOS，padding 使用 EOS，attention mask 只标记有效 token。

本例接受可打印 ASCII 英文，进行两次 HTML 反转义、空白合并和小写化；超过 75 个正文 token 会报错，便于调用方显式缩短描述。它没有实现任意 Unicode 的 ftfy 清洗，也不适用于中文 CLIP 模型。

| 输出 | 内容 |
| --- | --- |
| `index.html` | 内嵌图片、候选描述、余弦相似度、候选组分数 |
| `result.json` | 分类、反向检索、embedding、模型 hash、初始化及流水线耗时 |
| `text-tensors/`、`image-batch-*/` | 加 `--dump-tensors true` 时保存的实际输入输出及形状 |

候选组分数是余弦相似度乘以模型 logit scale 后的 Softmax。**改变候选组就会改变分数，它不是物体存在概率。** 业务拒识阈值需要在自己的数据上确定。

## 实测

2026-11-01，Linux x64、gfx1100、ROCm 7.2.1、MIGraphX 2.15.0、.NET 10.0.300、FP32。3 张商品图、6 条描述，image batch=4，预热 3 次，测量 20 次：

| 图片 | 第一候选 | 余弦相似度 |
| --- | --- | ---: |
| 运动鞋 | running-shoe | 0.2741 |
| 腕表 | wristwatch | 0.3070 |
| 拍立得相机 | instant-camera | 0.3577 |

三张图整批流水线平均 **20.42 ms**，约 **146.91 张/秒**。计时包含 CPU 缩放/归一化、GPU 图像编码、主机与设备复制、CPU 特征归一化和排序；排除文件解码、文本特征初始化、模型编译和导出。它不等同于单张模型推理延迟，也没有使用 HIP 图像前后处理。

整批 P50 为 **20.30 ms**，P95 为 **21.16 ms**；原始样本保存在运行结果 JSON 中。

同一份输入张量与 ONNX Runtime CPU 对照，图像/文本特征最大绝对误差分别约 `6.64e-7/3.58e-7`；六条描述的 BPE ID 和 mask 与独立分词实现逐项一致。该结果说明本次样例的数值对应关系，不是分类准确率评测。
