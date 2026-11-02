# MIGraphX CSharp API documentation

MIGraphX CSharp API is a managed API for AMD MIGraphX. Start with the [installation and first inference guide](guides/getting-started.md), then use the [managed object workflow](guides/managed-objects.md) to compose explicit model, target, argument and output resources.

| Task | Guide |
| --- | --- |
| Install the native runtime or diagnose loading | [Deployment](guides/runtime-deployment.md) |
| Configure dynamic inputs and reuse compiled programs | [Dynamic shapes and cache](guides/dynamic-shapes-cache.md) |
| Construct typed operation attributes | [Operation attributes](guides/operation-attributes.md) |
| Submit work on a HipSharp stream | [HIP integration](guides/hip-interop.md) |
| Use graph, TensorFlow or quantization APIs | [Advanced APIs](guides/advanced-apis.md) |
| Choose a framework or assess support | [Frameworks](compatibility/frameworks.md), [platforms](compatibility/platforms.md) |
| Look up types and methods | [API reference](api/index.md) |
| Review releases, compatibility and upgrade instructions | [Release history](release-notes/README.md) |
| Read Chinese release and application articles | [Technical blog](blog/README.md) |
| Run CLIP image/text matching | [CLIP sample](../samples/Clip/README.md) |
| Parse document layout, OCR and tables | [PP-Structure sample](../samples/PPStructure/README.md) |

Download version `0.0.2` from [GitHub Releases](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v0.0.2). Install the coherent ROCm 7.2.1 / MIGraphX 2.15.0 system runtime separately. Real-runtime qualification is partial; the platform guide describes the limits that affect application choices.

中文提示：请先阅读入门和部署指南；高级 API 的存在不代表所有真实运行语义已经验证。
