# HIP asynchronous integration

Reference `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop`, the matching MIGraphX core version and HipSharp `0.10.0`. Use one coherent ROCm installation and device for HIP and MIGraphX.

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

Enumerate `GetParameterShapes()` **after compilation** and supply every compiled parameter. With `offloadCopy: false`, MIGraphX can add output or scratch parameters beyond the ONNX inputs; these also require device allocations. Initialize the actual model inputs, and retain the compiler-added storage through completion. The vision samples demonstrate this allocation pattern.

Raw device pointers, free-form backend names, graph capture and pooled/async allocations are excluded. Native stream submission uses the fixed `ihipStream_t` C ABI spelling.

## Keep outputs on the GPU (0.0.2)

`RunDeviceIntoAsync` accepts device inputs and one caller-owned `HipDeviceMemory` destination for each `program.GetOutputShapes()` entry. Compile with `offloadCopy: false`. Outputs must have static, standard packed shapes, sufficient capacity, and use the same HIP runtime and device as the stream. Destinations cannot alias an input or another destination.

```csharp
var shapes = program.GetOutputShapes();
var destinations = shapes.Select(shape => hip.Allocate((ulong)shape.ByteCount)).ToArray();
try
{
    using var run = program.RunDeviceIntoAsync(inputs, destinations, stream);
    // Enqueue your HIP kernels here on the same stream, using destinations.
    // The model outputs reach these buffers through ordered device-to-device copies.
    run.Synchronize();
    // Copy only the final postprocessed results to the host when needed.
}
finally
{
    stream.Synchronize();
    foreach (var buffer in destinations) buffer.Dispose();
}
```

This path performs D2D copies, not zero-copy output borrowing. It creates no host tensor snapshots; `run.Outputs` throws because results reside in the supplied device buffers. Input/output owners, program state and native outputs stay leased until stream completion, including after a partial copy submission failure. Later kernels must use the same stream or an explicit synchronization boundary. Synchronize before reusing those destinations on another stream.

The YOLO26 and PaddleOCR v4/v5/v6 samples use this path for HIP preprocessing, inference and postprocessing. Their `--backend cpu` option retains the original CPU preprocessing/postprocessing baseline. See [vision samples](vision-samples.md).

Bounded official-runtime checks cover host completion, multiple submissions, stream isolation, early disposal leases, device-input reference output and input rejection on the fixed gfx1100 baseline. These do not establish zero-copy, overlap or arbitrary workload performance; see [platform support](../compatibility/platforms.md).

中文提示：主机输入启用 offload-copy，设备输入关闭 offload-copy。`RunDeviceAsync` 完成后复制输出到主机；`RunDeviceIntoAsync` 将输出 D2D 复制到调用方缓冲区，可继续在显存中处理。异步提交不代表已经完成推理，设备资源应保留到 stream 完成。
