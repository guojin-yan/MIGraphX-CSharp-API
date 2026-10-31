# Managed objects and ownership

Native resources use explicit instances and deterministic `Dispose`. Every resource participating in a call must select the same absolute native-library path.

## Synchronous inference

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var nativePath = "/opt/rocm-7.2.1/lib/libmigraphx_c.so.3";
var model = File.ReadAllBytes("/absolute/path/to/model.onnx");

using var parseOptions = new MIGraphXOnnxOptions(nativePath);
using var program = MIGraphXProgram.ParseOnnxBuffer(model, parseOptions);
var inputShapes = program.GetParameterShapes();

using var target = new MIGraphXTarget(nativePath, "gpu");
using var compileOptions = new MIGraphXCompileOptions(nativePath, offloadCopy: true);
program.Compile(target, compileOptions);

using var input = MIGraphXArgument.Create(
    nativePath, inputShapes["input"], new[] { 1f, 2f, 3f, 4f });
using var parameters = new MIGraphXParameterMap(nativePath);
parameters.Add("input", input);

using var outputs = program.Run(parameters);
float[] copied = outputs[0].ToArray<float>();
```

Use your model's actual parameter names and shapes. Add one argument for every parameter; the parameter map must match the complete native name set. Outputs preserve native ordering.

Execution checks each input's scalar type and concrete shape before calling native run. Static inputs must match both lengths and exact strides, including strides on length-one axes. Dynamic inputs must have the declared rank, with every length inside its inclusive range; optimal values are tuning hints and do not restrict accepted lengths. A mismatch throws `ArgumentException` identifying the parameter. Host execution also requires owned host-backed arguments.

Synchronous `Run` requires `offloadCopy: true` because its inputs and detached outputs are host buffers. It rejects a program compiled with `offloadCopy: false` before native execution; use the HIP device-input adapter for that mode. Finish any pending asynchronous runs on the same program before calling `Run`.

## Copies and lifetimes

`GetParameterShapes` and `GetOutputShapes` return independent managed snapshots. `MIGraphXShape` owns no native handle. Static snapshots include scalar type, lengths, strides, rank, element/byte counts, and layout flags.

`ElementCount` counts logical elements. `ByteCount` describes storage: explicit strides include padding, while zero strides let broadcast elements share storage. For float32 lengths `[2, 3]`, strides `[4, 1]` need 28 bytes; strides `[0, 1]` need 12 bytes. The public typed host-argument factories continue to require standard packed shapes.

Strides on length-one axes do not affect standard-layout classification. For example, lengths `[2, 1, 3]` with strides `[3, 0, 1]` are standard and packed; their exact strides are preserved when passed to native argument and graph APIs.

`MIGraphXArgument.Create<T>` copies the input array. `MIGraphXParameterMap.Add` deep-copies its argument. Synchronous run outputs are copied before the native collection is released. `ToArray<T>` requires the exact supported scalar mapping; tuple and bool/half/bfloat16/float8 typed host copies are rejected.

Dispose programs, options, targets, arguments, parameter maps and output collections. Access to disposed native resources fails. For the HIP adapter, resources remain leased until stream completion; see [HIP integration](hip-interop.md).

## Explicit content comparison

```csharp
bool sameInput = input.HasSameNativeContent(anotherHostBackedArgument);
bool samePrintedProgram = program.HasSameNativeContent(anotherProgram);
```

Argument comparison requires owned host-backed values under the same native root and compares exact shape/data without tolerance. Program comparison follows the fixed runtime's printed-structure implementation. Neither method establishes model inference equivalence or changes .NET `Equals`, hashing or operators.

Use [dynamic shapes and cache](dynamic-shapes-cache.md) for input overrides and persistence, [operation attributes](operation-attributes.md) for typed operations, and [advanced APIs](advanced-apis.md) for graph views and experimental features.

中文提示：显式创建并释放原生资源；Shape、名称和同步输出是独立快照，输入与参数表分别拥有副本。比较 API 的语义有明确边界，不能据此判定模型推理等价。
