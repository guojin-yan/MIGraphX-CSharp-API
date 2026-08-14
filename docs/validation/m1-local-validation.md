# M1 local validation status

The M1 Direct P/Invoke subset has passed local managed, ABI, fake-native, package, and documentation gates. This page is deliberately not an official MIGraphX runtime result.

| Evidence level | Result | What was verified |
| --- | --- | --- |
| `statically-verified` | passed locally | Frozen header and official ELF hashes, six-symbol subset parity, C ABI declarations, generated interop source, and no exported fake-native binaries in the package. |
| `fake-native-executed` | passed locally | Loader behavior, error classification, strict UTF-8, status propagation, ownership cleanup, repeated disposal, assignment copy behavior, and parallel target/program lifecycles. |
| `runtime-executed` | passed separately | At `f1a11cfd1701a041cee29188f7600c85b34ae260`, the official library loaded and valid `gpu` target/program create, assign, and destroy executed. |

The fake library is a test substitute built outside source control. It can prove the managed binding contract and failure paths, but cannot prove the ROCm installation, real MIGraphX behavior, ONNX execution, or GPU behavior.

The completed official session used a detached checkout of the pushed full SHA, verified the pinned header and native binary, ran `tools/radeon/cloud-test.sh`, and retained only redacted public evidence. No endpoint, credential, hostname, IP address, device identifier, model binary, or customer data is committed.

中文说明：M1 本地门禁和独立官方 runtime 会话均已通过。fake-native 仍只验证托管绑定和错误路径；官方结论绑定精确 SHA 和单一环境，不能扩展到其他平台或版本。
