# M8 API baseline and prerelease-readiness design

M8 establishes an exact managed compatibility baseline without changing the native inventory. It is not a permanent API freeze: reviewed interface batches continue on the `0.x.x` line and update the snapshot, ownership map, tests, and cloud-validation plan together. At the original M8 checkpoint, the core had 27 public types and 151 member/enum/constant records, the optional HIP adapter had 3 public types and 11 members; the current M12 batch additionally has a four-TFM core clean-consumer audit and a managed-only adapter consumer smoke. The high-level inventory remains 75 supported, 116 planned, and one unsupported C-variadic operation.

## API review decision

The review covers names, parameter order, optional defaults, generic constraints, nullable metadata, exceptions, bilingual XML summaries, resource ownership, repeated disposal, failure cleanup, and concurrent lease boundaries. During `0.x.x`, every intentional addition must identify its native entry points, ownership, local contract tests, and exact cloud runtime evidence boundary. Breaking changes still require an explicit decision record.

The schema 2 public snapshots record exact types and declared public/protected members, including modifiers, interfaces, defaults, generic constraints, nullable metadata, package/assembly identity, and the 15-TFM availability policy. `eng/verify-public-api.ps1` reflects every core and adapter assembly and fails on any unreviewed drift; an approved interface batch regenerates and reviews the snapshot. Internal `LibraryImport`/`DllImport` implementation differences are not public TFM differences.

Owned handles remain idempotently disposable. Borrowed shapes and collection items do not release parent-owned native handles. Copied values and host outputs remain valid independently of the native collection that produced them. M6 keeps programs, maps, arguments, device-memory leases, stream callbacks, and native outputs alive until completion or disposal establishes the stream boundary. `TryComplete` is a query, `Synchronize` is a completion boundary, and device input still requires an explicit D2H output copy.

## Version strategy

The managed SemVer is independent of the installed ROCm/MIGraphX version. The repository default stays `0.0.0`; local prerelease candidates are built with the explicit `0.9.0-rc.1` parameter and subsequent development remains below `1.0.0`. A final `1.0.0` does not mean ROCm 1.0, guarantee all MIGraphX releases, or upgrade APT packages. It requires completed Windows runtime validation and a separate explicit owner instruction.

For a candidate, NuGet version and `MIGraphXBuildInfo.PackageVersion` equal the requested SemVer. Assembly and file versions use its numeric core (`0.9.0.0` for this candidate), while informational version is `<semver>+<40-character-source-commit>`. The default cache managed identity includes the managed package version, so a package upgrade cannot silently reuse an entry created by another managed build. The cache envelope remains schema 1 because its serialized fields are unchanged.

The adapter uses exact closed dependencies on `[0.9.0-rc.1]` core and `[0.9.1]` HipSharp for every TFM. Release restore source mapping requires both JYPPX packages to come from the local feed; nuget.org may only supply Microsoft reference assemblies.

Local behavior tests can use `eng/prepare-hipsharp-baseline.ps1` to archive the exact read-only HipSharp commit into ignored artifacts and pass that root to `eng/build.ps1`. This keeps a neighboring HipSharp worktree and its uncommitted changes out of M8 evidence. Package construction never consumes that source snapshot; it consumes only the exact hash-reviewed `0.9.1` nupkg.

## Native compatibility policy

The only declared native baseline is Ubuntu 24.04 amd64, ROCm 7.2.1, and MIGraphX `2.15.0.70201-81~24.04`. A native upgrade requires a fresh header/export/ABI review plus M1-M6 system-native execution and compatibility review. Updating the managed package never upgrades the system installation.

.NET Framework and end-of-support .NET assets are compile/package compatibility statements only. Windows and macOS loader candidates remain diagnostics, not supported native platforms. A process may activate only one MIGraphX root; a second path fails before probing or loading it. Diagnostic objects are returned to the caller and are never logged automatically, but explicit paths and platform-loader text can be sensitive and must be redacted before external publication.

## Package and evidence policy

Core and adapter candidates contain 15 DLL/XML pairs, README, LICENSE, NOTICE, repository commit, and release notes, with no source, PDB, model, cache, test, or native payload. The RC intentionally has no icon, source package, or symbol package; no unreviewed asset is introduced. Deterministic compilation and `PathMap` are enabled, but nupkg ZIP bytes are not claimed to be reproducible.

`eng/verify-release-candidate.ps1` audits packages and isolated consumers, scans restored NuGet dependencies for known vulnerabilities, and emits a product CycloneDX SBOM, package manifest, and local SLSA-shaped provenance. Evidence records both the ZIP SHA-256 and a normalized identity over sorted package entry paths, sizes, and hashes. The provenance is local and unsigned; it is not a publication attestation.

The adapter package-reference workflow restores and builds through a version-scoped `artifacts/adapter-pack-obj/<version>` intermediate directory (and a separate package cache). This keeps package-mode assets out of the standard source-reference restore state; a subsequent source-mode build can run with `--no-restore` without requiring a repair restore.

The maximum status without a newly authorized official host is `release-candidate-local`. It is not `release-candidate-runtime`, `release-ready`, `published`, or permission to create a tag, GitHub release, NuGet upload, Pages deployment, or Actions run.

中文摘要：M8 建立可审查的版本化 API 基线，不是永久冻结；后续在 `0.x.x` 下继续封装接口，并同步更新快照、ownership、测试与云端验证计划。托管 SemVer 与 ROCm/MIGraphX 系统版本独立，`1.0.0` 还要求 Windows 实机验证和 Owner 明确授权；适配器包模式还原使用按版本隔离的中间目录，避免污染标准源码还原；产品 SBOM/provenance 只证明本地 managed 候选，不提升官方运行证据。
