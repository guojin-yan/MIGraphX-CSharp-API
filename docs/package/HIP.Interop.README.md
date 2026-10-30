# MIGraphX HIP interop adapter

`JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` is an optional managed adapter. It depends on the matching MIGraphX core version and exact HipSharp `[0.9.1]`; neither core package depends on the other. It contains managed DLL/XML assets for 15 frameworks and no native runtime.

`MIGraphXHipExecution` provides `RunHostAsync` for programs compiled with `offloadCopy=true` and `RunDeviceAsync` for validated `HipDeviceMemory` input with `offloadCopy=false`. Both return a `MIGraphXHipAsyncRun`.

Native submission uses the fixed `ihipStream_t` backend. `TryComplete` polls; `Synchronize` waits; disposing a pending run also waits. Outputs become available only after completion. Program, parameter map, argument and device-buffer leases remain held until then.

Device completion explicitly copies outputs to owned host arguments. Arbitrary external pointers, graph capture and pooled/async allocations are excluded. Full official async/device-buffer runtime qualification is pending; no zero-copy, overlap or performance guarantee is made.

Use the same coherent AMD ROCm system installation and device for both cores. See [the HIP integration guide](https://github.com/guojin-yan/MIGraphX-CSharp-API/blob/main/docs/guides/hip-interop.md).
