# Validation evidence

M1 through M9 evidence is recorded in `compatibility/runtime-validation-matrix.json` and keeps execution evidence separate from distribution policy:

- `statically-verified`: frozen package/header hashes and official ELF symbol inspection;
- `fake-native-executed`: the local C test substitute exercised managed lifecycle, loader, workflow, or isolated ABI patterns;
- `runtime-executed`: reserved for a real native call in a redacted, authorized environment record.
- `not-applicable`: the capability is deliberately outside the product boundary, as with the rejected Runtime NuGet distribution.

The local fake library is never committed, packed, or described as a real MIGraphX result. Separately, the redacted official session at `346cdd0b01a7f8039f5deb93058928403fccc7dd` revalidated the fixed official library, M1 target/program lifecycle, and restricted M2 ONNX/GPU workflow while completing the independently reviewed M9 option smoke.

See [M1 local validation status](m1-local-validation.md), [M2 local validation status](m2-local-validation.md), [M3 local validation status](m3-local-validation.md), [M4 local validation status](m4-local-validation.md), [M5 local validation status](m5-local-validation.md), [M6 local validation status](m6-local-validation.md), [M7 local deployment status](m7-local-validation.md), [M8 local candidate status](m8-local-validation.md), [M9 cloud interface plan](m9-cloud-validation.md), and the [M1/M2 official runtime summary](m1-m2-official-runtime.md).

The official session used a clean detached checkout of a pushed full SHA, verified the installed header and library, and returned only redacted evidence. No connection details, credentials, hostnames, or device identifiers belong here. Future stages require fresh authorization; the completed M1/M2 authorization is not reusable.

中文摘要：静态、fake-native、managed RC 与官方 runtime 证据保持分层；M8 的本地 API/包/SBOM 门禁和 M9 local option tests 都不会自动提升官方 runtime 证据。M1/M2 结论只覆盖记录中的环境和受限 Identity 工作流；后续官方执行仍需重新授权。
