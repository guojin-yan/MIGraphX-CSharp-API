# Validation evidence

M1 local evidence is recorded in `compatibility/runtime-validation-matrix.json` and keeps three boundaries separate:

- `statically-verified`: frozen package/header hashes and official ELF symbol inspection;
- `fake-native-executed`: the local C test substitute exercised managed lifecycle and loader behavior;
- `runtime-executed`: reserved for a real native call in a redacted, authorized environment record.

No official MIGraphX library, target/program lifetime, ONNX operation, or AMD GPU runtime has been executed in this workspace. The local fake library is never committed, packed, or described as a real MIGraphX result.

See [M1 local validation status](m1-local-validation.md) for the publishable local summary and the exact boundary before an authorized real-runtime session.

Before an official M1 session, a pushed full SHA must exist. The Owner must explicitly authorize the session and provide current connection information. The cloud checkout must be detached at that SHA, verify the installed header, run the M1 smoke/ABI commands, and record redacted results in the outer `Radeon_Cloud` workspace. No connection details, credentials, hostnames, or device identifiers belong here.

中文摘要：当前只有静态和 fake-native 本地证据。真实环境需要 Owner 明确授权、已推送完整 SHA、detached checkout 与脱敏记录；没有这些条件不得把 M1 写为 completed。
