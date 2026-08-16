# MIGraphXSharp documentation

MIGraphXSharp has an unpublished M10 `0.9.0-rc.2` local candidate while retaining `0.0.0` as the repository default. M8 established a reviewable compatibility baseline; M10 intentionally extends it on the `0.x.x` line. Final `1.0.0` requires completed Windows runtime validation and explicit owner authorization.

Local fake-native execution remains a test substitute. Separately, unified M1/M2 official runtime validation was revalidated at pushed commit `346cdd0b01a7f8039f5deb93058928403fccc7dd` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The result covers the official loader, M1 lifecycle, and the restricted M2 Identity file/buffer parse, GPU compile, synchronous run, and reference comparison.

Start with [Getting started](guides/getting-started.md), [API compatibility and versioning](guides/api-versioning.md), and [Runtime deployment](guides/runtime-deployment.md), then review the [M8 design](design/m8-api-release-readiness.md), [M8 local validation](validation/m8-local-validation.md), [platform evidence](compatibility/platforms.md), [official M1/M2 runtime summary](validation/m1-m2-official-runtime.md), and [M0-M8 retrospective](articles/m0-m8-evidence-driven-wrapper.md).

The current M9 interface batch adds five ONNX/compile options and moves the aggregate map to 80 supported, 111 planned, and one unsupported item. Its [design](design/m9-inference-options.md), [reviewed cloud validation](validation/m9-cloud-validation.md), and [article draft](articles/m9-interface-options-cloud-record.md) keep local failure/cleanup evidence separate from the bounded official setter-acceptance and Identity result.

M10 adds a copied ONNX parser-registry snapshot and explicit host-argument/program native content comparison. Shape equality remains planned. The aggregate map is 84 supported, 107 planned, and one unsupported item. Review the [M10 design](design/m10-onnx-registry-native-comparison.md), [local validation](validation/m10-local-validation.md), [future runtime plan](validation/m10-runtime-plan.md), and [engineering article](articles/m10-explainable-c-api-introspection.md). No new official host was authorized, so M10 remains `runtime-deferred`.

中文摘要：M8 为 managed API 建立可审查基线，并补齐版本、local-feed、SBOM/provenance 与候选门禁；接口继续在 `0.x.x` 下扩展。M7 的 system-native 策略保持不变，精确 SHA 的官方 runtime 结论仍只覆盖 M1/M2 受限工作流。
