# Validation evidence

M1/M2 local evidence is recorded in `compatibility/runtime-validation-matrix.json` and keeps three boundaries separate:

- `statically-verified`: frozen package/header hashes and official ELF symbol inspection;
- `fake-native-executed`: the local C test substitute exercised managed lifecycle and loader behavior;
- `runtime-executed`: reserved for a real native call in a redacted, authorized environment record.

The local fake library is never committed, packed, or described as a real MIGraphX result. Separately, the redacted official session at `f1a11cfd1701a041cee29188f7600c85b34ae260` executed the fixed official library, M1 target/program lifecycle, and the restricted M2 ONNX/GPU workflow.

See [M1 local validation status](m1-local-validation.md), [M2 local validation status](m2-local-validation.md), and the [M1/M2 official runtime summary](m1-m2-official-runtime.md).

The official session used a clean detached checkout of a pushed full SHA, verified the installed header and library, and returned only redacted evidence. No connection details, credentials, hostnames, or device identifiers belong here. Future stages require fresh authorization; the completed M1/M2 authorization is not reusable.

中文摘要：静态、fake-native 与官方 runtime 证据保持分层。M1/M2 已在精确 SHA 的授权会话通过，但结论只覆盖记录中的环境和受限 Identity 工作流；后续阶段必须重新授权。
