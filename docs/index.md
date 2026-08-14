# MIGraphXSharp documentation

MIGraphXSharp `0.0.0` contains M1 lifecycle, the M2 restricted ONNX workflow, the M3 low-level binding pipeline, M4 resource-safe synchronous objects, M5 dynamic-shape/cache policy, the optional M6 HipSharp native-async adapter, and M7 fail-closed Runtime supply-chain infrastructure. The frozen 192-item inventory remains 75 supported, 116 planned, and one unsupported high-level item.

Local fake-native execution remains a test substitute. Separately, unified M1/M2 official runtime validation passed at pushed commit `f1a11cfd1701a041cee29188f7600c85b34ae260` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The result covers the official loader, M1 lifecycle, and the restricted M2 Identity file/buffer parse, GPU compile, synchronous run, and reference comparison.

Start with [Getting started](guides/getting-started.md), the [managed object workflow](guides/managed-objects.md), and [Runtime deployment](guides/runtime-deployment.md), then review the [M6 async/HIP design](design/m6-hip-async-interop.md), [M7 Runtime design](design/m7-runtime-packaging.md), [M7 local validation](validation/m7-local-validation.md), [platform evidence](compatibility/platforms.md), and the [official M1/M2 runtime summary](validation/m1-m2-official-runtime.md).

中文摘要：M7 已冻结 managed-only + AMD 官方 system-native 安装策略，Runtime NuGet 为 `not-applicable`，候选包与 marker loader 已移除。精确 SHA 的四条官方 runtime 结论仍只覆盖 M1/M2 受限工作流。
