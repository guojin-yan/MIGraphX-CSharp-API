# M6 local validation

M6 is locally closed at two evidence levels only: `statically-verified` and `fake-native-executed`. No Radeon Cloud or other official MIGraphX runtime session was authorized or executed.

## Static closure

- Official fixed source: AMDMIGraphX `rocm-7.2.1`, commit `de19b73ad280476e646512b847885eda100ec35e`.
- Frozen C header remains SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`.
- The generated ABI remains 158 matching `LibraryImport`/`DllImport` EntryPoints with one variadic unsupported declaration.
- `migraphx_program_run_async` preserves `void*` stream and `const char*` name widths and is required only when the M6 path is used.
- The M6 map is `75 supported / 116 planned / 1 unsupported`; three experimental context/queue entries remain planned.
- The adapter public baseline is exactly three types and eleven declared public members, with bilingual XML and no raw pointer, SafeHandle, generated delegate, or internal HIP API type.
- Both core assemblies grant friend access only to `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop`; neither core references the other.

## Executed substitute behavior

The local fake MIGraphX implementation queues work and leaves output buffers pending until an explicit per-stream completion hook. The fake HIP implementation supplies distinct owned stream handles, NotReady/Success query transitions, device ordinals, memory ownership, and injected D2H failures.

The M6 tests execute:

- fixed `ihipStream_t` and non-null stream identity at native enqueue;
- output access rejection before completion and passive `IsCompleted`;
- NotReady to completed transitions and idempotent result disposal;
- early caller disposal of program, map, host argument, and device memory while leases remain alive;
- same-stream multiple-run completion with independent output snapshots;
- distinct stream completion isolation;
- blocking result synchronization, pending result disposal, and stream disposal establishing the completion boundary;
- device pointer identity through parameter-map assignment;
- exact name, shape, capacity, runtime-client, device-ordinal, and capture validation;
- enqueue failure with a returned collection, success with null, null borrowed output, and output-count drift;
- explicit post-completion D2H and injected D2H failure as the terminal primary error;
- all tracked native collection/program/device-memory live counts returning to their expected baselines.
- abandoned completed results releasing owned host snapshots through a non-blocking internal finalizer.

HipSharp separately executes its internal atomic enqueue tests: a pending callback remains retained across NotReady, completes after Success, is idempotent, rejects graph capture, and does not register failed enqueue work.

## Package boundary

The unpublished adapter candidate is audited for all 15 DLL/XML asset pairs, root README/LICENSE, exactly two NuGet dependencies in every TFM group, and absence of source, PDB, test, native, runtime, model, artifact, or machine-path payloads. A clean `net10.0` package-only consumer compiles both host async and `HipDeviceMemory` input surfaces.

## Evidence boundary

The fake libraries are test substitutes and cannot establish official MIGraphX execution, GPU overlap, performance, or zero-copy behavior. The latest reviewed session at `346cdd0b01a7f8039f5deb93058928403fccc7dd` covers synchronous M1/M2 Identity plus the separate M9 option smoke. M6 still adds no runtime-executed row.

中文摘要：M6 本地测试证明托管状态机、stream 回调、early-dispose、指针租约、显式 D2H 和故障清理，但没有执行官方 MIGraphX/GPU，也没有 zero-copy 或性能结论。
