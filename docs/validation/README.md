# Validation evidence

M1 through M12 evidence keeps execution evidence separate from distribution policy. Historical official records remain in `compatibility/runtime-validation-matrix.json`; M10 post-build promotion, M11 cases, and the M12 runtime plan are separate machine-readable evidence:

- `statically-verified`: frozen package/header hashes and official ELF symbol inspection;
- `fake-native-executed`: the local C test substitute exercised managed lifecycle, loader, workflow, or isolated ABI patterns;
- `runtime-executed`: reserved for a real native call in a redacted, authorized environment record.
- `not-applicable`: the capability is deliberately outside the product boundary, as with the rejected Runtime NuGet distribution.

The local fake library is never committed, packed, or described as a real MIGraphX result. The redacted session at `346cdd0b01a7f8039f5deb93058928403fccc7dd` revalidated M1/M2 and M9. A later post-build record at `e2386dc69e7640f8ff12d95284e56c3f02c87938` independently reviewed the four adopted M10 registry/comparison entry points. Neither record covers M4-M6 hardening.

See [M1 local validation status](m1-local-validation.md), [M2 local validation status](m2-local-validation.md), [M3 local validation status](m3-local-validation.md), [M4 local validation status](m4-local-validation.md), [M5 local validation status](m5-local-validation.md), [M6 local validation status](m6-local-validation.md), [M7 local deployment status](m7-local-validation.md), [M8 local candidate status](m8-local-validation.md), [M9 cloud interface validation](m9-cloud-validation.md), [M10 local validation](m10-local-validation.md), [M10 runtime plan](m10-runtime-plan.md), [M11 runtime hardening plan](m11-runtime-hardening-plan.md), [M12 real-runtime validation plan](m12-runtime-validation-plan.md), and the [M1/M2 official runtime summary](m1-m2-official-runtime.md).

The official session used a clean detached checkout of a pushed full SHA, verified the installed header and library, and returned only redacted evidence. No connection details, credentials, hostnames, or device identifiers belong here. Future stages require fresh authorization; the completed M1/M2 authorization is not reusable.

中文摘要：静态、fake-native、managed RC 与官方 runtime 证据保持分层。M10 post-build 外部记录已独立复核四个 adopted 入口；M11 只冻结 M4-M6 package-only 方案，尚无新官方授权，不能提升 M8。
