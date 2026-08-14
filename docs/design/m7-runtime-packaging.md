# M7 Runtime package and native closure

M7 establishes an auditable Runtime supply-chain boundary without claiming that a distributable package exists. The current technical state is `runtime-deferred`; `candidateStaged`, `verified`, `publishAuthorized`, and `releaseAuthorized` are all false. Core `0.0.0` and the optional M6 adapter remain managed-only.

## Signed source lock

The first reviewed RID is Ubuntu 24.04 amd64 `linux-x64`, ROCm 7.2.1, and MIGraphX `2.15.0.70201-81~24.04`. `linux-x64.json` pins the HTTPS repository, AMD archive-key fingerprint and hash, `InRelease`, `Packages.gz`, and every indexed package name/version/architecture/URL/size/SHA-256. Acquisition refuses a non-AMD host, redirect, wrong architecture, metadata drift, cache hash drift, and a cache or staging path outside the repository.

The signed root package is `migraphx-rpath7.2.1`, SHA-256 `071eeaf5653447b3328298ab5800e069534c620d01eae5dcad4ec50777cc85d9`. Its audited C root is `libmigraphx_c.so.3.0.70201`, SHA-256 `581582270fe1a8bb323eba04fb23f2969bbcdcff4a2a92d501eba7adf6a349ac`, SONAME `libmigraphx_c.so.3`, ELF64 x86-64, with upstream RPATH `$ORIGIN:$ORIGIN/../lib:$ORIGIN/../lib/migraphx/lib`. Six canonical MIGraphX ELF files and six materialized aliases are recorded. Alias entries reuse the canonical source path, size, and hash because NuGet ZIP does not preserve Debian symbolic links.

## Closure and system boundary

The dependency report separates official-ELF `DT_NEEDED` edges from fixed-source plugin edges and package-only traces that remain pending. The proposed user-mode closure includes the MIGraphX C/core/frontend/GPU/device files plus exact HIP/HSA/HIPRTC, MIOpen, rocBLAS, hipBLASLt, rocRAND, and rocTracer providers. Ubuntu supplies the declared minimum glibc, libgcc, libstdc++, libm, and zlib system dependencies. The host, never the package, supplies `amdgpu`/`amdkfd`, firmware, `/dev/kfd`, and `/dev/dri`.

M7 records only one redistributable license from the extracted MIGraphX root. The provider payload and license inventories are incomplete, so the manifest cannot become a candidate allowlist yet. The SBOM describes this deferred inventory; it does not state that missing components are licensed or staged.

## Topology decision and size gate

The preferred topology is `layered-deferred`: future package `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.linux-x64` version `7.2.1` would depend on exact `JYPPX.ROCm.HIP.CSharp.API.Runtime.linux-x64` `[7.2.1]` and carry only MIGraphX/provider increments. Version `7.2.1` expresses ROCm lockstep; the separate `nativeVersion` records MIGraphX identity.

A self-contained package was rejected because it duplicates HIP/HSA/COMGR assets and still exceeds the reviewed size gate. An unversioned shared ROCm package was rejected because M7 cannot introduce a cross-project package/API/version contract without an independent review. The indexed incremental source archives total 2,195,081,068 bytes; hipBLASLt alone is 1,613,836,012 bytes, compared with a 262,144,000-byte package limit. No split or RPATH rewrite is approved.

## Metadata and promotion boundary

`generate-runtime-metadata.ps1` deterministically emits CycloneDX 1.5 and provenance. The declared content digest covers sorted native-file and license identities plus the SBOM hash. Provenance binds the manifest, closure, SBOM, signed repository inputs, baseline source commit, state, and blockers. The manifest deliberately does not hash provenance or a final nupkg, avoiding a self-reference cycle.

`pack-runtime.ps1`, `eng/pack.ps1 -Runtime`, and direct `dotnet pack` all fail with `MIGRAPHX1001`. No MSBuild property can enable the deferred project. A future candidate requires an external clean-SHA attestation; publication and release require a separate Owner-reviewed receipt bound to the exact nupkg, manifest, SBOM, native family, environment, model, commands, and exits.

## Loader failure boundary

The existing explicit and system-native paths remain available. The new reserved Runtime layout is `runtimes/linux-x64/native/lib`; it is checked before the legacy RID directory, application directory, and system loader. A file in the reserved layout must be accompanied by `runtimes/linux-x64/native/migraphx-runtime-closure.xml`.

The marker must identify the exact package ID, version, RID, ROCm family, manifest content digest, and every closure file path/hash/SONAME. The loader validates all entries before loading the root. A missing root, marker, dependency, changed hash, path traversal, wrong family, or second active directory fails closed and prevents system fallback. This local guard does not replace package-only `readelf`, loader trace, and process-map evidence.

## Evidence limit

M7 adds signed-source and root-ELF static evidence plus local negative tests. It does not produce a Runtime nupkg, clean Runtime consumer, package-only trace, official GPU execution, or M4-M6 runtime upgrade. The four historical `runtime-executed` claims remain bound only to `f1a11cfd1701a041cee29188f7600c85b34ae260` and its restricted synchronous M1/M2 Identity workflow.

中文摘要：M7 已固定签名源、MIGraphX 根 ELF、分层拓扑、SBOM/provenance 和 loader 失败关闭契约，但 provider 闭包/许可证、体积、package-only 布局与新官方主机验证未闭合，因此状态严格保持 `runtime-deferred`，没有 Runtime nupkg。
