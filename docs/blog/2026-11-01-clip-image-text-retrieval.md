# 用 C# 在 AMD GPU 上运行 CLIP：从商品图片到图文检索

## 前言

前面介绍 MIGraphXSharp 时，我们主要看的是检测和 OCR。这次换一个方向：如果没有预先训练好的商品分类器，只给模型几句描述，能不能把一批图片按内容分出来？

CLIP 就适合做这类尝试。图片和文字分别通过编码器，得到同一特征空间中的向量，再比较相似度。换一组描述，就能换一组检索条件，不需要修改分类头。

我把这个过程写成了一个独立 C# 示例。本文使用三张商品照片和六条英文描述，完成图到文匹配、文到图检索，并给出 AMD GPU 上的实际耗时。源码位于 main 分支的 `samples/Clip`，核心 API 沿用 `0.0.2`。

## 一、先看运行结果

![CLIP 三张商品图的实际匹配结果](assets/clip/clip-results.png)

图中照片与分数来自本次实际运行；照片使用条件与来源保留在示例下载脚本和 README 中。

候选描述是运动鞋、腕表、拍立得相机、背包、水瓶和耳机。三张图的第一候选如下：

| 输入 | 第一候选 | 余弦相似度 |
| --- | --- | ---: |
| 运动鞋照片 | running-shoe | 0.2741 |
| 腕表照片 | wristwatch | 0.3070 |
| 拍立得相机照片 | instant-camera | 0.3577 |

这几张图都匹配到了预期类别，但三张图片不能用来评价模型准确率。实际业务中，还需要加入不属于任何候选类别的图片，检查拒识阈值。

程序输出 `index.html`，可以直接查看图片、六条描述的排序和分数。`result.json` 另存反向检索结果和 512 维 embedding，后续接向量数据库时可以复用。

## 二、两套编码器怎样接入

图像编码器接收 `[batch,3,224,224]`，文本编码器接收 `[描述数量,77]` 的 `input_ids` 和 `attention_mask`。它们都输出 512 维特征。

在示例中，我用独立的 `ModelSession` 封装模型解析、输入形状设置、GPU 编译和执行。下面这段来自实际程序，先对候选描述编码一次：

```csharp
var tokens = new ClipTokenizer(models)
    .Encode(job.Prompts.Select(p => p.Text).ToArray());

using var text = new ModelSession(
    native,
    Path.Combine(models, "clip-vit-b-32.text-encoder.onnx"),
    new Dictionary<string, long[]>
    {
        { "input_ids", [job.Prompts.Length, 77] },
        { "attention_mask", [job.Prompts.Length, 77] }
    });

var textInputs = new Dictionary<string, Array>
{
    { "input_ids", tokens.Ids },
    { "attention_mask", tokens.Mask }
};
var features = ClipProcessing.Normalize(
    text.Run(textInputs)[0].Floats, job.Prompts.Length);
```

同一批候选描述可以服务多张图片，不需要每张图都重复运行文本模型。图像编码器按固定 batch 运行，尾批补零，补出的行不会进入业务结果。

这里有两个容易出错的地方。图像应先把短边缩放到 224，再居中裁剪；先裁原图再缩放会改变采样。文本需要模型配套的 BPE 词表、BOS/EOS 和 attention mask，不能把单词序号直接传进去。

当前 tokenizer 接受可打印 ASCII 英文，正文最多 75 个 token，另外两个位置留给 BOS/EOS。中文检索需要匹配的中文 CLIP 模型及 tokenizer，不能仅把输入字符串换成中文。

## 三、准备并运行

测试环境使用 Linux x64、.NET 10.0.300、ROCm 7.2.1、MIGraphX 2.15.0 和 gfx1100 GPU，模型为 FP32 的 OpenAI CLIP ViT-B/32。

仓库地址：`https://github.com/guojin-yan/MIGraphX-CSharp-API`。

在仓库根目录执行：

```bash
bash samples/Clip/fetch-assets.sh /data/clip
dotnet build samples/Clip/Clip.csproj -c Release \
  -p:JYPPXManagedTargetFrameworks=net10.0

dotnet run --project samples/Clip -c Release --no-build -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --models /data/clip/models --manifest /data/clip/inputs.json \
  --output /results/clip --batch 4 --warmup 3 --iterations 20
```

下载脚本会准备双编码器、词表、许可和三张演示图片。模型来源为 `https://github.com/guojin-yan/DeploySharp/releases/tag/models-visual.1`，演示图片来自 Unsplash，具体地址保留在脚本中。

改自己的输入时，只需修改清单中的 `prompts` 和 `images`。描述变化后要重新计算文本特征；图片路径相对清单文件所在目录解析，输出请放在源码仓库外。

## 四、速度应该怎样看

2026-11-01 实测：三张图、六条描述，image batch=4，预热 3 次、测量 20 次，整批平均 **20.42 ms**，折合 **146.91 张/秒**。

整批 P50 为 **20.30 ms**，P95 为 **21.16 ms**。程序在 JSON 中保留每次样本，换设备或调整 batch 后可以按同一口径重新比较。

这个数字包含 CPU 图像缩放和归一化、GPU 图像编码、主机与设备之间的复制、CPU 特征归一化与排序。文件解码、文本特征初始化、模型编译和 HTML 导出没有放进去。因此它表示三张图的处理吞吐，不能写成“单张 CLIP 推理 20.42 ms”。

本例的图像前后处理仍在 CPU，编码器在 MIGraphX GPU。以后接入 HIP 时，应在相同图片、相同 batch 和相同计时范围下比较，不能只拿模型内部时间替代整体时间。

我还把程序保存的真实输入张量交给 ONNX Runtime CPU 对照。图像编码器最大绝对误差约 `6.64e-7`，文本编码器约 `3.58e-7`；六条描述的 BPE ID 和 mask 与独立实现逐项一致。这一步检查的是两条实现是否对应，不能替代业务数据评测。

## 五、接入应用时保留哪些数据

做图片检索时，可以将图片 embedding 预先写入索引，用户输入描述后只计算文本特征；做固定类别分类时，则缓存候选文本特征。两种场景都应记录模型版本和预处理配置，避免混用不同模型生成的向量。

还有一点需要注意：HTML 里的候选组分数来自 Softmax。候选越多、描述越接近，分数就可能变化；它不是“这张图有 99% 的概率是一双鞋”。示例同时保留余弦相似度，方便在自己的数据上设置阈值和复核规则。

完整参数与输出说明在仓库的 `samples/Clip/README.md`。本例没有加入目标检测或抠图，图片按整图编码；需要主体裁剪时，应先准备合适的 ROI，再比较效果和额外耗时。
