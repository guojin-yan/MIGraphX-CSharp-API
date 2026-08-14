# Platforms and evidence levels

| Claim | Status | Meaning |
| --- | --- | --- |
| Managed build | M1 through M7 local validation | Both the core and optional adapter compile all 15 exact TFMs; M7 adds no public API |
| Package assets | M1 through M7 local validation | Each core/adapter TFM has a DLL and XML document; neither candidate contains native binaries |
| Clean managed consumer | M1 through M7 local validation | Core representative consumers and one adapter consumer restore/build from local package-only feeds |
| Frozen header and official ELF | Statically verified | All 159 header functions match the official Linux ELF; 158 have managed EntryPoints and one C-variadic declaration is explicitly unsupported |
| M3 normalized inventory | Statically verified | 159 functions, 2 enums, 25 handles, and 6 callbacks close over 192 mutually exclusive classifications |
| M3 critical ABI patterns | Fake-native executed | Callback lifetime/exception boundary, bool, size_t, UTF-8, borrowed/out pointers, array length, and cleanup ran against a minimal test substitute |
| Lifecycle and restricted ONNX workflow | Fake-native executed | A local C substitute ran loader, parse, shape, compile, run, output-copy, cleanup, and concurrency tests |
| M4 resource-safe objects | Fake-native executed | Owned handles, copied shapes, typed host buffers, parameter maps, multi-item output copying, failure injection, and Dispose behavior ran against the local substitute |
| M5 dynamic shape and cache | Fake-native executed | Dynamic ranges, overrides, Save/Load, cache integrity/corruption recovery, and concurrency ran against the local substitute |
| M6 async and HipSharp adapter | Fake-native executed | Native enqueue state, stream completion, early disposal, pointer leases, stream isolation, explicit D2H, and completion failures ran against local substitutes |
| M7 signed source/root ELF | Statically verified | AMD key, signed repository metadata, exact root package, six canonical ELF files plus aliases, dependency edges, one license, SBOM, and provenance are pinned |
| Official M1/M2 runtime | Runtime-executed | Passed at `f1a11cfd1701a041cee29188f7600c85b34ae260` on the single recorded Ubuntu/ROCm/MIGraphX/gfx1100 environment |
| Windows/macOS native runtime | Unverified diagnostic candidates | Candidates are honest loader diagnostics, not an official MIGraphX build/support claim |
| Official ONNX parse/compile/run | Runtime-executed | Generated Identity file and buffer paths compiled and ran synchronously with matching reference output |
| AMD GPU | Runtime-executed | One gfx1100 GPU executed the restricted static float32 Identity graph; this is not a general device claim |
| Runtime NuGet | Not applicable | Owner selected managed-only distribution and AMD official system installation; no Runtime package project, staging/promotion path, marker loader, or native payload exists |

The system-native policy targets the audited Ubuntu 24.04 amd64 metadata and Linux SONAME `libmigraphx_c.so.3`. The historical official record proves one exact system-installed host configuration: Ubuntu 24.04 x86-64, ROCm 7.2.1, MIGraphX `2.15.0.70201-81~24.04`, and gfx1100. It does not establish other distributions, versions, devices, models, dynamic shapes, M6 async/device-buffer, zero-copy, or performance paths.

中文摘要：M7 将 Runtime NuGet 标为 `not-applicable`，冻结 managed-only + AMD 官方系统安装策略，没有新增官方 runtime 结论。精确环境的 M1/M2 已运行，Windows/macOS 仍不因候选字符串存在而获得支持声明。
