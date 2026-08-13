# M1 local validation status

The M1 Direct P/Invoke subset has passed local managed, ABI, fake-native, package, and documentation gates. This page is deliberately not an official MIGraphX runtime result.

| Evidence level | Result | What was verified |
| --- | --- | --- |
| `statically-verified` | passed locally | Frozen header and official ELF hashes, six-symbol subset parity, C ABI declarations, generated interop source, and no exported fake-native binaries in the package. |
| `fake-native-executed` | passed locally | Loader behavior, error classification, strict UTF-8, status propagation, ownership cleanup, repeated disposal, assignment copy behavior, and parallel target/program lifecycles. |
| `runtime-executed` | pending authorization | A real `libmigraphx_c` load plus target/program create, assign, and destroy at the pushed commit SHA. |

The fake library is a test substitute built outside source control. It can prove the managed binding contract and failure paths, but cannot prove the ROCm installation, real MIGraphX behavior, ONNX execution, or GPU behavior.

An authorized official M1 session must use a detached checkout of a pushed full SHA, verify the pinned header and native binary, run `tools/radeon/cloud-test.sh`, and write only redacted results to the outer `Radeon_Cloud` record. No endpoint, credential, hostname, IP address, device identifier, model, or customer data may be committed.

中文说明：M1 本地门禁已通过，但真实 MIGraphX 运行时证据仍待 Owner 明确授权。fake-native 只验证托管绑定和错误路径，不能替代 ROCm、真实 MIGraphX、ONNX 或 GPU 结果。
