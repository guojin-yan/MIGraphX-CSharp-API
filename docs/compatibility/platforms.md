# Platform support and limitations

Install the coherent ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04` system packages on Ubuntu 24.04 x86-64. MIGraphXSharp packages contain managed assemblies only.

| Area | Application boundary |
| --- | --- |
| Linux native runtime | The fixed baseline has historical official execution for a restricted static float32 Identity workload on one gfx1100 GPU. Other models, distributions, versions and devices need application validation. |
| Windows native runtime | The fixed official MIGraphX baseline targets Linux. Windows managed builds and loader diagnostics do not supply an official Windows MIGraphX runtime. |
| macOS | No official native runtime support claim. |
| ONNX options | Loop limits/defaults, external-data roots, fast-math and tuning setters are exposed. Historical setter acceptance does not qualify dedicated model semantics, tuning or accuracy. |
| Registry and comparison | Copied ONNX parser names and exact host argument/native printed-program comparison are available. Parser names do not guarantee model support; comparison does not prove inference equivalence. |
| Dynamic shapes and cache | Managed APIs are implemented. Complete real-runtime shape, persistence, restart and resource-lifetime qualification remains pending. |
| HIP async/device input | Implemented optional adapter; full official runtime qualification is pending. Outputs are copied to host at completion. No zero-copy, overlap or performance guarantee. |
| Graph, TensorFlow and quantization | APIs are implemented; complete model semantics and lifecycle qualification remain pending. |
| Context and typed operation attributes | Historical focused execution covers context parent lifetime and five materialized attribute forms. This is limited to the tested source/provider configuration. |
| Custom operations | Experimental callbacks are exposed. Host-reference numerical diagnostics do not qualify GPU/device callback memory or complete provider lifetimes. |
| Reliability and performance | Historical functional, isolation, timing and partial long-run records exist. Full restart/resource-observation qualification remains incomplete; no performance comparison is claimed. |

Local substitute-library tests check managed ownership, validation and ABI handling. They are distinct from execution against the official native runtime. Historical results do not automatically qualify the current source revision.

General C variadic values are excluded; use [typed materialized operation attributes](../guides/operation-attributes.md). Independently owned `migraphx_module_create` is excluded because the frozen C ABI has no matching destroy/ownership contract; use program-owned module views. Arbitrary device pointers and graph capture are outside the supported managed boundary.

For installation and errors, see [deployment](../guides/runtime-deployment.md).

中文提示：目前真实 runtime 支持范围有限。高级 API 已实现与完整语义已经验证是两件事；请按本页边界选择功能，并对实际模型、设备和版本进行验证。
