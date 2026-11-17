using JYPPX.ROCm.MIGraphXSharp;

var environment = MIGraphXEnvironment.ProbeSystem(exerciseObjects: false);
var native = environment.LoadedPath;
if (!environment.ExportsComplete || native is null || !Path.IsPathFullyQualified(native) ||
    !Path.GetFullPath(native).StartsWith(Path.GetFullPath(AppContext.BaseDirectory),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
    throw new InvalidOperationException("The engine must load from the clean package consumer output: " +
        string.Join("; ", environment.Diagnostics.Select(d => d.Message)));
if (args.Length != 1) throw new ArgumentException("Pass the identity ONNX fixture path.");
var values = new[] { 0.25f, -1f, 2f, 9f };
using var options = new MIGraphXOnnxOptions(native);
using var program = MIGraphXProgram.ParseOnnxFile(args[0], options);
using var target = new MIGraphXTarget(native, "ref");
using var compileOptions = new MIGraphXCompileOptions(native);
program.Compile(target, compileOptions);
var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
using var input = MIGraphXArgument.Create(native, shape, values);
using var parameters = new MIGraphXParameterMap(native);
parameters.Add("input", input);
using var output = program.Run(parameters);
if (!values.SequenceEqual(output[0].ToArray<float>())) throw new InvalidOperationException("Packaged reference inference differs.");
Console.WriteLine("Clean runtime package discovery and reference inference passed: " + native);
