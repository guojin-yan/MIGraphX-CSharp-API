# MIGraphX HIP interop adapter

`JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` is an optional managed adapter. The `0.9.0-rc.2` candidate depends on exact `[0.9.0-rc.2]` MIGraphX core and exact `[0.9.1]` HIP core ranges; neither core package depends on the other. It contains DLL/XML assets for 15 target frameworks plus README, LICENSE, and NOTICE, with no native runtime.

The adapter exposes three public types: `MIGraphXHipExecution`, `MIGraphXHipAsyncRun`, and `MIGraphXHipDeviceInput`. `RunHostAsync` requires `offloadCopy=true`. `RunDeviceAsync` accepts validated `HipDeviceMemory` inputs and requires `offloadCopy=false`.

Native submission always uses the fixed `ihipStream_t` backend name required by the ROCm 7.2.1 MIGraphX C ABI. The result remains pending until `HipStream.Query`, `Synchronize`, or disposal establishes stream completion. Program, parameter map, arguments, native outputs, device pointers, and the stream callback remain leased during that interval. Outputs are independent owned host arguments after completion.

Device input does not imply zero-copy. Completion performs an explicit device-to-host output copy; the adapter does not use `Marshal.Copy` on device addresses and exposes no raw stream or memory pointer. Graph capture, arbitrary external pointers, pooled/async allocations, and runtime binaries are not supported by this candidate.

M6 evidence is limited to fixed-source inspection plus local fake MIGraphX/HIP execution. It does not prove official GPU execution, overlap, performance, or zero-copy behavior.
