# MIGraphXSharp documentation

MIGraphXSharp `0.0.0` contains M1 lifecycle, the M2 restricted ONNX workflow, the M3 low-level binding pipeline, M4 resource-safe synchronous objects, M5 dynamic-shape/cache policy, and the optional M6 HipSharp native-async adapter. The frozen 192-item inventory now maps to 75 supported, 116 planned, and one unsupported high-level item.

Local fake-native execution remains a test substitute. Separately, unified M1/M2 official runtime validation passed at pushed commit `f1a11cfd1701a041cee29188f7600c85b34ae260` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The result covers the official loader, M1 lifecycle, and the restricted M2 Identity file/buffer parse, GPU compile, synchronous run, and reference comparison.

Start with [Getting started](guides/getting-started.md) and the [managed object workflow](guides/managed-objects.md), then review the [M6 async/HIP design](design/m6-hip-async-interop.md), [M6 local validation](validation/m6-local-validation.md), [platform evidence](compatibility/platforms.md), and the [official M1/M2 runtime summary](validation/m1-m2-official-runtime.md).

中文摘要：M6 在固定 M3 inventory 之上增加可选的 HipSharp native async/device-input 适配器，使用 stream 完成回调、early-dispose 租约与显式 D2H 输出固化。M4/M5/M6 新证据限于本地 fake-native；精确 SHA 的四条官方 runtime 结论仍只覆盖 M1/M2 受限工作流。
