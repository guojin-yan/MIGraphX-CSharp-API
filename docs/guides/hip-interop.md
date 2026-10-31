# HIP asynchronous integration

Reference `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop`, the matching MIGraphX core version and HipSharp `0.9.1`. Use one coherent ROCm installation and device for HIP and MIGraphX.

## Host input

Compile the program with `offloadCopy: true`, create its parameter map, then submit it on a HipSharp stream:

```csharp
using JYPPX.ROCm.HipSharp;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;

using var hip = new HipRuntime();
using HipStream stream = hip.CreateStream();
using MIGraphXHipAsyncRun run = program.RunHostAsync(parameters, stream);
run.Synchronize();
float[] output = run.Outputs[0].ToArray<float>();
```

`TryComplete` polls without blocking. `Synchronize` blocks until completion. `Outputs` is available after completion; reading it while pending fails. Disposing a pending run waits for completion.

Until every submitted run on a program has completed through its stream, graph edits, recompilation, sorting, program cloning/saving and synchronous `Run` are rejected. A parameter map in use by any pending run also rejects `Add`. Completion or a submission/output-copy failure releases the corresponding guard; completion of one stream does not release another stream's guard. Early `Dispose` of the program or map remains supported through retained leases.

## Device input

Compile with `offloadCopy: false`, create a `MIGraphXHipDeviceInput` for every exact parameter name using `HipDeviceMemory`, then call `RunDeviceAsync`. The adapter checks concrete standard packed shapes, scalar type, capacity, runtime client and device ordinal. Static model inputs require exact lengths and strides. For dynamic model inputs with explicit managed ranges, supply a concrete shape whose rank and lengths match those ranges; optimal values do not restrict valid input lengths. These checks run before native submission, and the model shape is checked again under the program lock during submission.

Device input is borrowed until completion. The adapter retains program, arguments, map, native output and device-memory leases while work is pending. Completion explicitly copies device outputs into independently owned host arguments.

Raw device pointers, free-form backend names, graph capture and pooled/async allocations are excluded. Native stream submission uses the fixed `ihipStream_t` C ABI spelling.

Full official async/device-buffer runtime qualification remains pending. The API provides no zero-copy, overlap or performance guarantee; see [platform support](../compatibility/platforms.md).

中文提示：主机输入启用 offload-copy，设备输入关闭 offload-copy。执行结果完成前由租约保活，完成后显式复制输出到主机；异步提交不代表已经完成推理。
