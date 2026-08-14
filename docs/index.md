# MIGraphXSharp documentation

MIGraphXSharp `0.0.0` contains the M1 lifecycle foundation, M2 restricted ONNX workflow, and M3 reproducible low-level binding pipeline for the official AMD MIGraphX C API. M3 closes a 192-item frozen-header inventory and emits matching 158-EntryPoint `LibraryImport` and `DllImport` paths across the exact 15-TFM build.

Local fake-native execution remains a test substitute. Separately, unified M1/M2 official runtime validation passed at pushed commit `f1a11cfd1701a041cee29188f7600c85b34ae260` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The result covers the official loader, M1 lifecycle, and the restricted M2 Identity file/buffer parse, GPU compile, synchronous run, and reference comparison.

Start with [Getting started](guides/getting-started.md), then review the [M3 binding generator](design/m3-binding-generator.md), [M3 local validation](validation/m3-local-validation.md), [platform evidence](compatibility/platforms.md), and the [official M1/M2 runtime summary](validation/m1-m2-official-runtime.md).

中文摘要：M3 已将固定头的 192 个实体闭合分类，并由 normalized model 同源生成两套各 158 个 EntryPoint。M3 新证据限于静态 ABI/ELF 和 fake-native 模式；精确 SHA 的官方 runtime 结论仍只覆盖 M1/M2 受限工作流。
