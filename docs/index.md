# MIGraphXSharp documentation

MIGraphXSharp has an unpublished M11 `0.9.0-rc.7` local candidate while retaining `0.0.0` as the repository default. M11 adds no public API: core remains 27/160, adapter 3/11, and the aggregate map 84/107/1. Final publication still requires explicit Owner authorization and closed release-readiness evidence.

Local fake-native execution remains a test substitute. Separately, unified M1/M2 official runtime validation was revalidated at pushed commit `346cdd0b01a7f8039f5deb93058928403fccc7dd` on Ubuntu 24.04 x86-64, ROCm 7.2.1, the frozen MIGraphX package, and one gfx1100 GPU. The result covers the official loader, M1 lifecycle, and the restricted M2 Identity file/buffer parse, GPU compile, synchronous run, and reference comparison.

Start with [Getting started](guides/getting-started.md), [API compatibility and versioning](guides/api-versioning.md), and [Runtime deployment](guides/runtime-deployment.md), then review the [M8 design](design/m8-api-release-readiness.md), [M8 local validation](validation/m8-local-validation.md), [platform evidence](compatibility/platforms.md), [official M1/M2 runtime summary](validation/m1-m2-official-runtime.md), and [M0-M8 retrospective](articles/m0-m8-evidence-driven-wrapper.md).

The current M9 interface batch adds five ONNX/compile options and moves the aggregate map to 80 supported, 111 planned, and one unsupported item. Its [design](design/m9-inference-options.md), [reviewed cloud validation](validation/m9-cloud-validation.md), and [article draft](articles/m9-interface-options-cloud-record.md) keep local failure/cleanup evidence separate from the bounded official setter-acceptance and Identity result.

M10 adds a copied ONNX parser-registry snapshot and explicit host-argument/program native content comparison. Shape equality remains planned. Its later post-build record independently promoted the four adopted entry points to `runtime-executed` for one exact host/build. Review the [M10 design](design/m10-onnx-registry-native-comparison.md), [local validation](validation/m10-local-validation.md), [runtime plan](validation/m10-runtime-plan.md), and [engineering article](articles/m10-explainable-c-api-introspection.md).

M11 synchronizes that external promotion and freezes deterministic M4-M6 fixtures, cases, thresholds, package-only probe, cache restart, review, and Windows policy. The rc.6 official functional session completed 20 cases with 18 passing and two dynamic-shape failures; it remains immutable diagnostic evidence and promotes no individual case. Rc.7 repairs the native shape snapshot ordering, but has no official authorization, so M11 remains `runtime-deferred` and M8 remains `release-candidate-local`. See the [M11 runtime hardening plan](validation/m11-runtime-hardening-plan.md).

中文摘要：M11 `0.9.0-rc.7` 不新增公开 API；它保留 rc.6 的 18/20 官方功能失败记录，并修复动态 shape 快照在判断动态状态前调用静态属性的问题。rc.7 没有新的官方授权，M11 为 `runtime-deferred`，M8 仍为 `release-candidate-local`。
