# From M0 to M8: an evidence-driven native wrapper

MIGraphXSharp did not begin by wrapping every declaration. M0 fixed the repository boundary, upstream identity, license, build matrix, and vocabulary for claims. That made later omissions visible instead of accidental.

M1 established the smallest loader and lifecycle path. M2 added one restricted ONNX Identity workflow. Both used generated local inputs and explicit ownership before an authorized Ubuntu/ROCm/gfx1100 session established the only current official runtime evidence. The result was deliberately narrow: loader, target/program lifecycle, file/buffer parse, GPU compile, synchronous run, and reference comparison.

M3 converted the frozen header into a normalized inventory and generated both interop paths. The important result was closure, not volume: 159 functions, 158 managed EntryPoints, and one explicit unsupported variadic declaration. Static official ELF parity and fake-native ABI tests answered different questions and remained different evidence classes.

M4 introduced resource-safe objects only where ownership could be explained. Owned SafeHandles, borrowed values, copied shapes, parameter maps, and output collections were tested through failure and disposal. M5 then made dynamic dimensions, serialization, and cache identity explicit. Cache keys bind the model, header/API, managed version, native fingerprint, target, options, format, and ordered overrides; corruption becomes a visible rebuild rather than an invisible hit.

M6 crossed into HipSharp without merging the two cores. Its small adapter owns the coupling: fixed `hipStream_t`, enqueue/completion separation, stream callback lifetime, device-memory leases, and explicit D2H output. Device input is not zero-copy, enqueue is not completion, and local substitutes do not prove GPU overlap or speed.

M7 rejected a Runtime NuGet after auditing the actual ROCm closure. Managed-only plus system-native installation was the more honest ownership boundary: AMD's signed package repository owns dependency resolution, file placement, provider licenses, and native upgrades. Historical feasibility manifests remain evidence for that decision, not product SBOMs or dormant package inputs.

M8 baselines what exists. Machine-readable API records compare nullable/default/generic contracts across 15 TFMs, while reviewed `0.x.x` interface batches can update that baseline. Candidate versions bind DLL identity, public build identity, cache identity, nuspec, and source commit. Adapter dependencies are exact because its lease and ABI assumptions are exact. Product SBOM/provenance describe only the managed bytes actually considered for release, and normalized content identity is kept separate from the nupkg ZIP hash.

The central lesson is that a native wrapper has several contracts at once: C ABI, managed ownership, package composition, system deployment, and evidence scope. A green build proves only one of them. The project reaches `release-candidate-local` when those local contracts agree; it reaches runtime or release readiness only after separately authorized, source-bound system-native execution and independent review.

中文摘要：M0-M8 的主线不是单纯追求 API 数量，而是把 ABI、ownership、package、system-native 与 evidence 分开治理。M8 建立可随 `0.x.x` 接口扩展而审查更新的 managed 基线；真实 M4-M6、长跑和性能仍等待新的明确授权。
