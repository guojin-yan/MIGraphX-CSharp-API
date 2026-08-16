# M7 local system-native validation

M7 is complete as a deployment-policy stage: native NuGet distribution is `not-applicable`, and the supported distribution boundary is managed-only packages plus an AMD official system installation. This does not upgrade M4-M6 to official runtime evidence.

## Local gates

The standard build/test/package suite verifies that:

- all 15 managed TFMs build with no native assets;
- core and adapter packages contain only their reviewed managed assets and dependencies;
- `pack.ps1 -Runtime` permanently rejects the unsupported distribution mode with `MIGRAPHX1001`;
- the Runtime package project, package verifier, marker loader, and candidate/promotion scripts do not exist;
- compatibility and deployment documents state `system-native` and keep historical runtime evidence scoped to its exact commit.

With the previously populated Git-ignored cache, the optional source audit remains:

```powershell
.\eng\test-runtime-source.ps1
```

It verifies the exact AMD source URLs and key fingerprint, signed `InRelease`, signed `Packages.gz` binding, exact root package metadata/bytes, and Debian archive shape. Eleven mutations cover package hash/version, architecture, host, repository version, key fingerprint, `InRelease`, traversal, absolute paths, symlink escape, and special devices. The audit performs no NuGet staging.

## Verified facts

- Deployment mode: `system-native`; Runtime NuGet: `not-applicable`.
- Managed package boundary: no AMD or fake-native binary in core or adapter packages.
- Audited source: Ubuntu 24.04 Noble amd64, ROCm 7.2.1.
- MIGraphX root: `migraphx-rpath7.2.1` `2.15.0.70201-81~24.04`, 68,651,368 bytes.
- Root C ELF: SHA-256 `581582270fe1a8bb323eba04fb23f2969bbcdcff4a2a92d501eba7adf6a349ac`, SONAME `libmigraphx_c.so.3`, ELF64 x86-64.
- Official M1/M2 execution was revalidated at `346cdd0b01a7f8039f5deb93058928403fccc7dd`; this remains separate from the M7 package/source audit.

## Not claimed

The repository does not test installation or removal through APT on the local Windows host. No new official-host session was authorized, so M4 objects, M5 Save/Load/cache, M6 host/device async, other devices/models, dynamic-shape workloads, zero-copy, overlap, and performance remain outside official runtime evidence.

The native package manager owns the full closure and licenses. The historical package-feasibility SBOM is retained only as an audit snapshot and is not a product SBOM for bytes distributed by this project.

中文摘要：M7 以“managed-only + AMD 官方系统安装”完成部署决策；Runtime NuGet 为 `not-applicable`。本地门禁证明仓库没有原生分发入口，签名来源审计仍可复核，但 M4-M6 没有新增官方运行证据。
