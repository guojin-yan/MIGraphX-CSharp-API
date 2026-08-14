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

M4 accepts static mapped scalar tensors. Dynamic or tuple shapes and bool/half/bfloat16/float8 families are rejected. The older `MIGraphXOnnxWorkflow` retains its exact single-input, single-output, standard float32 behavior and remains the shortest compatibility path.

中文摘要：所有原生资源都用显式实例和同一绝对库路径创建。Shape/名称/输出在原生集合释放前复制，typed input 与 parameter map 都拥有独立副本。M4 不公开动态 shape、异步或设备 buffer。
