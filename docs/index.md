# MIGraphXSharp documentation

MIGraphXSharp `0.0.0` contains the M1 lifecycle foundation and M2 restricted ONNX workflow for the official AMD MIGraphX C API. Forty-one cumulative declarations, strict UTF-8, status/loader semantics, native ownership, file/buffer parse, static float32 shapes, compile, and synchronous run are locally verified across the 15-TFM build.

Local fake-native execution is a test substitute. It does not prove that the official `libmigraphx_c.so.3`, an AMD GPU, ONNX parsing, compilation, or inference works. The Owner deferred unified M1/M2 official runtime execution until a later authorized session checks a pushed SHA.

Start with [Getting started](guides/getting-started.md), then review [M2 restricted ONNX workflow](design/m2-onnx-workflow.md), [platform evidence](compatibility/platforms.md), and [validation evidence](validation/README.md).

中文摘要：M1 生命周期基础与 M2 受限 ONNX 闭环已通过本地 loader/fake-native/ABI/package 门禁；本地替身不能代替官方 MIGraphX runtime。真实环境统一验证已按 Owner 决定后置，阶段状态为 `runtime-deferred`。
