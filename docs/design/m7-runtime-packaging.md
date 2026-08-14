# M7 system-native deployment decision

M7 originally evaluated a native Runtime nupkg. The reviewed closure is too large, duplicates ROCm assets already governed by AMD's package repository, and would require the managed project to own provider licenses, RPATH layout, cross-package family identity, and native upgrade policy. The Owner therefore selected `system-native` as the permanent deployment mode.

Core `0.0.0` and the optional M6 adapter remain managed-only. No `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.*` project, nupkg, staging state, closure marker, promotion receipt, or native package publication path exists.

## Retained source evidence

The first reviewed host remains Ubuntu 24.04 amd64, ROCm 7.2.1, and MIGraphX `2.15.0.70201-81~24.04`. The signed-source audit pins the AMD repository, archive-key fingerprint and hash, `InRelease`, `Packages.gz`, exact package names/versions/architectures/URLs/sizes/SHA-256 values, and the required `migraphx-rpath7.2.1` root package.

`prepare-runtime.ps1` is now verification-only. It validates official source metadata and the root Debian archive in a Git-ignored cache; it never stages files for NuGet. The archive guard rejects rooted/traversing paths, escaping symbolic links, ambiguous separators/control characters, hard links, devices, sockets, and FIFOs.

The tracked manifest, CycloneDX document, provenance, dependency report, and size study under `nuget/runtime-manifests` are a historical feasibility snapshot from the rejected package design. They are not an active package allowlist or promotion state machine.

## Distribution boundary

The managed packages contain no AMD binary, fake native library, driver, firmware, model, cache, compiler, header, debug payload, or Runtime dependency. Users install a coherent MIGraphX/ROCm family through AMD's official system repository. APT owns dependency resolution, file placement, licenses, and native package upgrades.

Ubuntu supplies the declared system libraries. The host supplies the AMD kernel driver, firmware, `/dev/kfd`, and `/dev/dri`. The managed project does not copy or redistribute any part of that boundary.

## Loader boundary

The package-only `runtimes/linux-x64/native/lib` marker branch has been removed. The established explicit path, application RID directory, application base, and system-loader candidates remain. The loader verifies the frozen managed export requirements and returns structured native errors, but it does not download files, alter process search paths, or prove that an arbitrary system installation is compatible.

An explicit absolute path is the preferred deterministic deployment contract. `ProbeSystem` remains available for diagnostics when the administrator has configured the OS loader coherently.

## Evidence limit

The system-native decision closes the M7 distribution question; Runtime NuGet is `not-applicable`, not deferred. Static source/root-ELF evidence and the historical system-installed M1/M2 execution remain valid at their exact recorded commits. M4-M6 still have no new official runtime session, and this decision adds no zero-copy, performance, device, model, or cross-version claim.

中文摘要：M7 已决定永久不提供 Runtime NuGet。managed core/adapter 只含托管资产，用户通过 AMD 官方仓库安装一致的 MIGraphX/ROCm；仓库保留签名来源审计，但删除候选包、marker loader、staging 和 promotion 表面。
