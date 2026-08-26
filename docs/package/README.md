# JYPPX.ROCm.MIGraphX.CSharp.API release candidate

This unpublished local prerelease candidate contains managed assets and XML documentation for 15 exact target frameworks. The current M12 interface-expansion code has passed local managed/fake-native compilation and focused project-quality tests, but remains development-only, has no real-runtime validation, and is not published. It carries no AMD or fake-native binary, model, cache payload, source, PDB, or generated inventory. M6 native async remains in the separate optional `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` candidate, so the core has no runtime NuGet dependency. M7 selects AMD official system installation; its source audit is not copied into this managed package.

M5 supports immutable dynamic-dimension metadata, explicit ONNX static/dynamic overrides, and fixed-version `msgpack` Save/Load. `MIGraphXModelCache` requires an explicit root and validates a versioned sidecar and payload hash. Cache entries are not portable across native fingerprints, targets, compile options, or package versions. Evidence is local fake-native only; no new official runtime claim is included.

- Native declarations: 158 matching `LibraryImport`/`DllImport` EntryPoints generated from one normalized model; one C-variadic header function explicitly unsupported
- M3 evidence: complete 192-item inventory and official ELF parity are static; isolated critical ABI patterns use a fake-native test substitute
- M4/M5 objects: explicit Program/Shape/Argument/Target/options/map/output plus dynamic/cache types with 74 supported low-level mappings and local fake-native ownership tests
- Optional M6 adapter: separate managed package with 75 supported mappings, fixed `ihipStream_t`, stream completion, early-dispose leases, `HipDeviceMemory` input, and explicit D2H output snapshotting
- M9 inference options: five additional high-level mappings for Loop defaults/limits, external-data paths, fast-math, and exhaustive tuning; aggregate coverage is 80 supported, 111 planned, and 1 unsupported
- M10 introspection/comparison: copied ONNX parser-registry names and explicit host argument/program comparison; shape equality remains planned and aggregate coverage is 84 supported, 107 planned, and 1 unsupported
- M12 local interface expansion: scalar/stride shapes, argument create/generate/load/save/clone, graph handles including the restricted operation factory/clone, typed materialized operation attributes for common scalars/vectors, TensorFlow options, quantization, experimental context, and raw custom-op callbacks; arbitrary C format placeholders and general varargs remain unsupported. Local fake-native evidence is recorded separately, while real runtime validation remains deferred and the additions are not included in the historical coverage counts
- Official native runtime: M1/M2 restricted workflow revalidated at `346cdd0b01a7f8039f5deb93058928403fccc7dd`; the separate M9 option smoke is bounded to setter acceptance and Identity integration, while M4/M5/M6 remain local test-substitute evidence
- ONNX parse/compile/run: generated Identity file/buffer paths executed with matching reference output on the recorded official environment
- AMD GPU validation: one gfx1100 device executed the restricted synchronous offload-copy workflow; no M6 async/device-buffer, zero-copy, or performance claim
- Native distribution: `system-native`; Runtime NuGet is `not-applicable`, and users install MIGraphX/ROCm from AMD's official system repository
- M8 identity: schema 2 reviewable API baseline, source-bound DLL/nuspec/cache version, managed product SBOM/provenance, and distinct ZIP/content hashes
- M10 evidence: local fake-native plus static source/ABI evidence only; official runtime remains deferred and the M9 authorization is not reused
- License: Apache-2.0; Copyright 2026 Guojin Yan

See the [source repository](https://github.com/guojin-yan/MIGraphX-CSharp-API) for the frozen manifest and validation limits.
