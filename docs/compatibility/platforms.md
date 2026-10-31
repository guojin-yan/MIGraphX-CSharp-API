# Platform support and limitations

Install the coherent ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04` system packages on Ubuntu 24.04 x86-64. MIGraphXSharp packages contain managed assemblies only.

| Area | Application boundary |
| --- | --- |
| Linux native runtime | Official execution on the fixed gfx1100 baseline covers static float32 reference models, all five YOLO26 tasks, and PP-OCRv6 tiny/small/medium detection/recognition. Other models, distributions, versions and devices need application validation. |
| Windows native runtime | The fixed official MIGraphX baseline targets Linux. Windows managed builds and loader diagnostics do not supply an official Windows MIGraphX runtime. |
| macOS | No official native runtime support claim. |
| ONNX options | Loop limits/defaults, external-data roots, fast-math and tuning setters are exposed. Historical setter acceptance does not qualify dedicated model semantics, tuning or accuracy. |
| Registry and comparison | Copied ONNX parser names and exact host argument/native printed-program comparison are available. Parser names do not guarantee model support; comparison does not prove inference equivalence. |
| Dynamic shapes and cache | Official bounded checks cover static/dynamic overrides, rejected invalid inputs, save/load/recompile, cold/warm cache lookup, identity/options misses, corruption rebuild, same-process writers and cache reuse in a fresh process. Native serialization remains version-bound; arbitrary shapes and concurrent cross-process writes are not qualified. |
| HIP async/device input | Official bounded checks cover host completion, multiple submissions, separate-stream isolation, early disposal leases, device-input reference output and input rejection. Outputs are copied to host at completion. No zero-copy, overlap or general throughput guarantee. |
| Graph, TensorFlow and quantization | Focused official graph, TensorFlow parsing and quantization checks have executed, including retained INT8/FP8 calibration snapshots. Model accuracy and arbitrary graph/provider semantics still require application validation. |
| Context and typed operation attributes | Historical focused execution covers context parent lifetime and five materialized attribute forms. This is limited to the tested source/provider configuration. |
| Custom operations | Experimental callbacks are exposed. Host-reference numerical diagnostics do not qualify GPU/device callback memory or complete provider lifetimes. |
| Reliability and performance | Functional/reference checks and repeated single-image measurements exist for the supplied vision models. These do not establish arbitrary workload throughput, long-term leak freedom, or GPU clock/power-controlled comparisons. |

Local substitute-library tests check managed ownership, validation and ABI handling. They are distinct from execution against the official native runtime. Historical results do not automatically qualify the current source revision.

Common integer, finite floating-point, Boolean, string, null and array operation attributes are implemented through the [typed attribute builder](../guides/operation-attributes.md); callers can also use its factories for reshape, transpose, slice, multibroadcast and topk. Arbitrary C format placeholders and variadic values are excluded. Supporting those would require a separately designed native ABI bridge, not a C# `params object[]` overload.

Program-owned modules are implemented and retain their parent's lifetime. An independent `migraphx_module_create` owner is excluded because the frozen C ABI has no matching destroy/ownership contract. The absence of a destroy export is not evidence that an independent allocation never needs release; calling an unrelated free/delete function would be unsafe. Use `GetMainModule` or `CreateModule` on a program. Arbitrary device pointers and graph capture are outside the supported managed boundary.

For installation and errors, see [deployment](../guides/runtime-deployment.md).

中文提示：动态形状、缓存和 HIP 异步/设备输入已有真实运行验证，不再笼统标为“待验证”。常用类型化属性和 program 管理的 module 可以使用；未开放的任意 varargs 和独立 module owner 属于接口契约边界。实验性 GPU 回调与任意工作负载的完整语义仍需分别验证。
