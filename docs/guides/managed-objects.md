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

## Copies and lifetimes

`GetParameterShapes` and `GetOutputShapes` return independent managed snapshots. `MIGraphXShape` owns no native handle. Static snapshots include scalar type, lengths, strides, rank, element/byte counts, and layout flags.

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
