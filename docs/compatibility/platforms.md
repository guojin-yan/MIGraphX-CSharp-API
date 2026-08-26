# Platforms and evidence levels

| Claim | Status | Meaning |
| --- | --- | --- |
| Managed build/API | M1 through M12 local validation | Core and adapter compile all 15 exact TFMs; the current schema 2 snapshots contain 45/303 and 3/11 records; M12 remains validation-deferred beyond local fake-native evidence |
| Package assets | M11 local candidate | Each rc.3 core/adapter TFM has a DLL and XML document; neither candidate contains native binaries |
| Clean managed consumer | M11 local candidate | Four representative core consumers, one adapter consumer, and the M11 runtime probe restore exact JYPPX dependencies from mapped local feeds |
| Managed SBOM/provenance | Statically verified | Source-SHA-specific product evidence records every managed package entry, exact dependencies/licenses/source commit, ZIP hash, and normalized content identity; provenance is local and unsigned |
| Frozen header and official ELF | Statically verified | All 159 header functions match the official Linux ELF; 158 have managed EntryPoints and one C-variadic declaration is explicitly unsupported |
| M3 normalized inventory | Statically verified | 159 functions, 2 enums, 25 handles, and 6 callbacks close over 192 mutually exclusive classifications |
| M3 critical ABI patterns | Fake-native executed | Callback lifetime/exception boundary, bool, size_t, UTF-8, borrowed/out pointers, array length, and cleanup ran against a minimal test substitute |
| Lifecycle and restricted ONNX workflow | Fake-native executed | A local C substitute ran loader, parse, shape, compile, run, output-copy, cleanup, and concurrency tests |
| M4 resource-safe objects | Fake-native executed | Owned handles, copied shapes, typed host buffers, parameter maps, multi-item output copying, failure injection, and Dispose behavior ran against the local substitute |
| M5 dynamic shape and cache | Fake-native executed | Dynamic ranges, overrides, Save/Load, cache integrity/corruption recovery, and concurrency ran against the local substitute |
| M6 async and HipSharp adapter | Fake-native executed | Native enqueue state, stream completion, early disposal, pointer leases, stream isolation, explicit D2H, and completion failures ran against local substitutes |
| M7 signed source/root ELF | Statically verified | AMD key, signed repository metadata, exact root package, six canonical ELF files plus aliases, dependency edges, one license, SBOM, and provenance are pinned |
| Official M1/M2 runtime | Runtime-executed | Revalidated at `346cdd0b01a7f8039f5deb93058928403fccc7dd` on the single recorded Ubuntu/ROCm/MIGraphX/gfx1100 environment |
| Windows native MIGraphX 2.15.0 | Not applicable | Fixed MIGraphX documentation applies to Linux; AMD's Windows HIP SDK component table marks AI libraries unavailable. HIP availability is not MIGraphX availability |
| macOS native runtime | Unverified diagnostic candidate | The candidate is an honest loader diagnostic, not an official MIGraphX build/support claim |
| Official ONNX parse/compile/run | Runtime-executed | Generated Identity file and buffer paths compiled and ran synchronously with matching reference output |
| AMD GPU | Runtime-executed | One gfx1100 GPU executed the restricted static float32 Identity graph; this is not a general device claim |
| Official M9 option smoke | Runtime-executed | At `346cdd0...`, the official runtime accepted five recorded option values and the reviewed Identity compile/run exactly matched the reference; dedicated option semantics remain planned |
| M10 registry and comparisons | Runtime-executed | The post-build external record at clean detached `e2386dc...` reviewed registry stability plus argument/program true/false/Dispose cases; shape equality remains planned |
| M11 M4-M6 hardening | Runtime-deferred | Deterministic fixtures, package-only probe, safe negative boundaries, fresh-process cache, long-run/timing budgets, and review gates are frozen but have no rc.3 official authorization |
| Runtime NuGet | Not applicable | Owner selected managed-only distribution and AMD official system installation; no Runtime package project, staging/promotion path, marker loader, or native payload exists |

The system-native policy targets the audited Ubuntu 24.04 amd64 metadata and Linux SONAME `libmigraphx_c.so.3`. The latest reviewed M10 record proves registry and native-comparison behavior on one exact system-installed configuration: Ubuntu 24.04 x86-64, ROCm 7.2.1, MIGraphX `2.15.0.70201-81~24.04`, and gfx1100. It does not establish other distributions, versions, devices, models, M4-M6 dynamic/async/device-buffer behavior, zero-copy, dedicated M9 semantics, long-run, or performance paths.

中文摘要：M10 的 post-build 官方记录已把 registry 与 argument/program comparison 提升为 `runtime-executed`；M11 不扩张 84/107/1 映射，只冻结 M4-M6 package-only 鲁棒性方案。固定 MIGraphX 2.15.0 的 Windows provider 依据 AMD 资料标为 `not-applicable`；M4-M6、其他环境、长跑与性能仍未执行。
