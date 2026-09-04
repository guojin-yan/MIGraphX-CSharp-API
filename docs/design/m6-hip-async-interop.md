# M6 native async and HipSharp interop

M6 adds an optional managed adapter without coupling either core package to the other:

```text
JYPPX.ROCm.MIGraphX.CSharp.API              no HipSharp dependency
JYPPX.ROCm.HIP.CSharp.API                   no MIGraphXSharp dependency
JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop  references both cores
```

The adapter package is an unpublished `0.0.0` candidate. It contains managed DLL/XML assets for the same 15 target frameworks as both cores, depends on MIGraphX core `0.0.0` and HIP core `0.9.1`, and contains no native runtime.

## Fixed upstream behavior

The implementation evidence is the official AMDMIGraphX `rocm-7.2.1` tag at commit `de19b73ad280476e646512b847885eda100ec35e`. The C++ API template accepts a HIP stream alias, while the fixed C ABI runtime identifies its underlying stream object as `ihipStream_t`; the adapter passes that ABI spelling to `migraphx_program_run_async`. The C entry point constructs an asynchronous `execution_environment`, while `program::eval` uses `wait_for` before evaluation and `finish_on` afterward. The returned arguments can therefore be obtained at enqueue time but their buffers are not treated as ready until the supplied HIP stream completes.

Sources: [C API implementation](https://github.com/ROCm/AMDMIGraphX/blob/de19b73ad280476e646512b847885eda100ec35e/src/api/api.cpp), [C++ type-name wrapper](https://github.com/ROCm/AMDMIGraphX/blob/de19b73ad280476e646512b847885eda100ec35e/src/api/include/migraphx/migraphx.hpp), [program async environment](https://github.com/ROCm/AMDMIGraphX/blob/de19b73ad280476e646512b847885eda100ec35e/src/program.cpp), and [upstream HIP async test](https://github.com/ROCm/AMDMIGraphX/blob/de19b73ad280476e646512b847885eda100ec35e/test/api/test_gpu.cpp).

The adapter exposes no free-form backend name. It always supplies the internal constant `ihipStream_t`, rejects null/disposed/capturing streams before enqueue, and does not expose `migraphx_context_finish`, the experimental context getter, or the borrowed queue pointer. `HipStream.Query` and `HipStream.Synchronize` are the completion boundaries.

## State and failure model

`MIGraphXHipAsyncRun` has one pending state and one terminal state. `IsCompleted` is passive and never queries HIP. `TryComplete` queries without blocking; `Synchronize` blocks. Completion snapshots every output into an independently owned host `MIGraphXArgumentCollection`, then releases the native output collection and all input leases exactly once. `Outputs` fails before completion, and all members fail after result disposal.

The lease set also has a non-blocking finalizer. This is required for abandoned pending work: its native handle leases and the adapter's borrowed device-pointer leases must be released even when the result and stream become unreachable before an explicit completion boundary. Explicit disposal remains idempotent and suppresses the finalizer.

Explicit `Dispose` blocks on the stream when work is pending. A HIP query/synchronize error leaves the result retryable because no completion boundary was established. An output snapshot or D2H error becomes the primary terminal error after native collections and leases have been released; repeated completion observes the same error. The internal completion-state finalizer never queries or synchronizes a stream; it only releases an already-completed owned output snapshot and never throws.

HipSharp supplies an internal, friend-only `EnqueuePending` operation. Enqueue and pending-callback registration execute under the stream lock, closing the race with stream disposal. The callback is released by existing `Query`, `Synchronize`, or `Dispose` behavior. Neither core exposes a new public raw handle or lease type.

## Host input path

`RunHostAsync` accepts an existing `MIGraphXParameterMap` and requires a program compiled with `offloadCopy=true`. Program, map, and every copied map argument hold SafeHandle references until completion, so caller disposal after enqueue is deferred safely. Completion copies host-readable native outputs into owned arguments.

This is native asynchronous submission, not `Task.Run` around synchronous execution. The fixed source establishes queue integration, while local fake-native tests establish the managed state and ownership behavior. Official GPU execution has not been performed for M6.

## Device input path and copy boundary

`RunDeviceAsync` intentionally supports only `HipDeviceMemory`. Each `MIGraphXHipDeviceInput` must have a unique exact parameter name, a concrete standard packed shape matching the compiled model, sufficient allocation capacity, the same HipSharp runtime client as the stream, and the same device ordinal. The program must use `offloadCopy=false`.

The adapter acquires HipSharp's existing SafeHandle-backed device pointer lease, creates a borrowed MIGraphX argument internally, and keeps the lease until stream completion. Caller disposal of the memory marks it for release but cannot free the pointer while native work is pending. The adapter owns neither the HIP stream nor device allocation.

After completion, device outputs are copied explicitly with `hipMemcpy(DeviceToHost)` into managed bytes and then into owned host arguments. `Marshal.Copy` is never used on a device address. The path is therefore:

```text
caller H2D -> borrowed device input -> MIGraphX enqueue/work -> explicit D2H -> owned host output
```

M6 does not claim zero-copy, fewer copies, overlap, or a performance improvement. Input pointer identity and lease ordering are tested only with local substitutes; official runtime measurements require a fresh authorized session.

## Evidence and exclusions

The M6 high-level map closes the frozen 192-item inventory as `75 supported / 116 planned / 1 unsupported`. Only `migraphx_program_run_async` advances in M6; `migraphx_argument_create` was already supported and gains an internal borrowed-buffer policy. Experimental context and queue functions remain planned.

Local evidence is `statically-verified` and `fake-native-executed`. M6 does not cover `HipAsyncDeviceMemory`, pooled allocations, graph capture, arbitrary streams or pointers, runtime NuGet, official GPU execution, performance, or deployment.
