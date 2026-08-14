# MIGraphXSharp documentation

MIGraphXSharp `0.0.0` contains the M1 lifecycle foundation and M2 restricted ONNX workflow for the official AMD MIGraphX C API. Forty-one cumulative declarations, strict UTF-8, status/loader semantics, native ownership, file/buffer parse, static float32 shapes, compile, and synchronous run are locally verified across the 15-TFM build.

Local fake-native execution remains a test substitute. Separately, unified M1/M2 official runtime validation passed at pushed commit `f1a11cfd1701a041cee29188f7600c85b34ae260` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The result covers the official loader, M1 lifecycle, and the restricted M2 Identity file/buffer parse, GPU compile, synchronous run, and reference comparison.

Start with [Getting started](guides/getting-started.md), then review [M2 restricted ONNX workflow](design/m2-onnx-workflow.md), [platform evidence](compatibility/platforms.md), and the [official runtime summary](validation/m1-m2-official-runtime.md).

中文摘要：M1 生命周期基础与 M2 受限 ONNX 闭环既通过本地 loader/fake-native/ABI/package 门禁，也在精确 SHA 的官方 MIGraphX/gfx1100 环境中完成统一 runtime 验证。该结论不能扩展到未测平台、模型或后续能力。
