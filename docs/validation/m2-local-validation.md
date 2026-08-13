# M2 local validation status

| Evidence level | Result | What was verified |
| --- | --- | --- |
| `statically-verified` | passed locally | Frozen header/official ELF parity for 41 cumulative exports, generated declarations, ownership manifest, C ABI widths, and deterministic model hash. |
| `fake-native-executed` | passed locally | File/buffer parse, shape validation, compile options, parameter map, pinned input, synchronous run, copied Identity output, frontend-missing diagnostics, injected failures, cleanup, and concurrency. |
| `runtime-executed` | deferred by Owner | Official MIGraphX loader plus M1 lifecycle and M2 file/buffer parse, GPU compile, run, and reference comparison at one pushed SHA. |

Representative fake execution covers `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0`, spanning generated `DllImport` and `LibraryImport` paths. The 128-byte Identity model is script-generated and never tracked or packed.

No official MIGraphX ONNX function or AMD GPU graph has executed locally. M2 is `runtime-deferred`, not completed; fake output cannot be promoted to official runtime evidence.

中文说明：M2 的 41 项 ABI、模型哈希和 fake-native 闭环已在本地通过，真实 MIGraphX/GPU 运行按 Owner 决定与 M1 合并后置。当前状态是 `runtime-deferred`，不是 completed。
