# M2 local validation status

| Evidence level | Result | What was verified |
| --- | --- | --- |
| `statically-verified` | passed locally | Frozen header/official ELF parity for 41 cumulative exports, generated declarations, ownership manifest, C ABI widths, and deterministic model hash. |
| `fake-native-executed` | passed locally | File/buffer parse, shape validation, compile options, parameter map, pinned input, synchronous run, copied Identity output, frontend-missing diagnostics, injected failures, cleanup, and concurrency. |
| `runtime-executed` | passed separately | Revalidated at `346cdd0b01a7f8039f5deb93058928403fccc7dd`: both parse paths, GPU compile, synchronous run, and Identity reference comparison executed. |

Representative fake execution covers `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0`, spanning generated `DllImport` and `LibraryImport` paths. The 128-byte Identity model is script-generated and never tracked or packed.

No official MIGraphX ONNX function or AMD GPU graph executes during local tests. The separate official result was promoted from `runtime-candidate-executed` only after evidence hashes and JSON fields were independently reviewed.

中文说明：M2 的 41 项 ABI、模型哈希和 fake-native 闭环已在本地通过，精确 SHA 的真实 MIGraphX/GPU 会话也已完成；两者证据仍分层，不扩大到动态 shape、异步或 device buffer。
