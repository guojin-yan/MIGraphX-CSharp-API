# MIGraphXSharp documentation

MIGraphXSharp `0.0.0` contains the M1 lifecycle foundation, M2 restricted ONNX workflow, M3 reproducible low-level binding pipeline, and M4 resource-safe synchronous object layer for the official AMD MIGraphX C API. M4 keeps the 192-item M3 inventory fixed and separately maps it to 52 supported, 139 planned, and one unsupported high-level item.

Local fake-native execution remains a test substitute. Separately, unified M1/M2 official runtime validation passed at pushed commit `f1a11cfd1701a041cee29188f7600c85b34ae260` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The result covers the official loader, M1 lifecycle, and the restricted M2 Identity file/buffer parse, GPU compile, synchronous run, and reference comparison.

Start with [Getting started](guides/getting-started.md) and the [managed object workflow](guides/managed-objects.md), then review the [M4 ownership design](design/m4-managed-object-model.md), [M4 local validation](validation/m4-local-validation.md), [platform evidence](compatibility/platforms.md), and the [official M1/M2 runtime summary](validation/m1-m2-official-runtime.md).

中文摘要：M4 在固定 M3 inventory 之上增加 owned handle、复制 shape、typed host buffer、parameter map 与同步输出对象。M4 新证据限于本地 fake-native；精确 SHA 的四条官方 runtime 结论仍只覆盖 M1/M2 受限工作流。
