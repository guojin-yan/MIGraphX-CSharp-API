# Managed object workflow

The M4 object layer composes explicit resource instances without exposing raw pointers. The native library path must be absolute and must identify the same loaded library for every participating resource.

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var nativePath = "/absolute/path/to/libmigraphx_c.so.3";
var model = File.ReadAllBytes("/absolute/path/to/model.onnx");

using var parseOptions = new MIGraphXOnnxOptions(nativePath);
using var program = MIGraphXProgram.ParseOnnxBuffer(model, parseOptions);
var inputShapes = program.GetParameterShapes();

using var target = new MIGraphXTarget(nativePath, "gpu");
using var compileOptions = new MIGraphXCompileOptions(nativePath, offloadCopy: true);
program.Compile(target, compileOptions);

using var input = MIGraphXArgument.Create(
    nativePath,
    inputShapes["input"],
    new[] { 1f, 2f, 3f, 4f });
using var parameters = new MIGraphXParameterMap(nativePath);
parameters.Add("input", input);

using var outputs = program.Run(parameters);
float[] copied = outputs[0].ToArray<float>();
```

`GetParameterShapes` and `GetOutputShapes` return managed snapshots; they do not keep native collections alive. `Create<T>` and `Add` both deep-copy their data. `Run` validates that supplied parameter names exactly match the native parameter set and returns outputs already detached from the native run collection.

M5 adds `MIGraphXDynamicDimension` ranges and explicit ONNX static/dynamic overrides. Dynamic snapshots expose ranges but no concrete element or byte count; choose a concrete static shape before creating a typed argument. Tuple and bool/half/bfloat16/float8 families remain rejected. The older `MIGraphXOnnxWorkflow` retains its exact single-input, single-output, standard float32 behavior and remains the shortest compatibility path.

M6 keeps the synchronous core API unchanged. Native asynchronous execution lives in the optional adapter, where program/map/argument handles are leased until HipStream completion and outputs become owned host snapshots. Device input is limited to validated `HipDeviceMemory` and has an explicit D2H output boundary. See the [M6 design](../design/m6-hip-async-interop.md).

M10 adds explicit comparison without changing object identity semantics:

```csharp
bool sameInput = input.HasSameNativeContent(anotherHostBackedArgument);
bool samePrintedProgram = program.HasSameNativeContent(anotherProgram);
```

Argument comparison requires the same loaded native root and owned host-backed values; it is exact and has no tolerance. Program comparison follows the fixed native printed-structure implementation. These methods do not override `Equals`, hashing, or operators and do not prove model or inference equivalence. Reverse comparisons acquire owner locks in a stable order and serialize with Dispose. `MIGraphXShape` remains an immutable managed snapshot without a native owner.

中文摘要：所有原生资源都用显式实例和同一绝对库路径创建。Shape/名称/同步输出在原生集合释放前复制，typed input 与 parameter map 都拥有独立副本。M6 异步能力通过租约保活并显式 D2H；M10 的 argument/program 比较使用稳定双 owner 锁序，保持显式、版本绑定语义，不覆写通用 equality。
