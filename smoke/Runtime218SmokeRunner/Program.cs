using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using JYPPX.ROCm.MIGraphXSharp;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using JYPPX.ROCm.HipSharp;

// Explicit opt-in native integration runner. It never substitutes fake libraries.
if (args.Length is < 4 or > 5) throw new ArgumentException("Usage: Runtime218SmokeRunner <migraphx-library> <identity.onnx> <hip-library> <external-output-directory> [general|fp16|bf16|int8|fp8|callbacks|soak]");
var native = Path.GetFullPath(args[0]);
var model = Path.GetFullPath(args[1]);
var hipPath = Path.GetFullPath(args[2]);
var output = Path.GetFullPath(args[3]);
Directory.CreateDirectory(output);
var checks = new List<object>();
var failures = 0;
var group = args.Length == 5 ? args[4] : "all";
if (!new[] { "all", "general", "fp16", "bf16", "int8", "fp8", "callbacks", "soak" }.Contains(group)) throw new ArgumentException("Unknown runtime verification group.");
string? activeCheck = null;
var capabilities = Enum.GetValues<MIGraphXFeature>().ToDictionary(feature => feature, feature => MIGraphXCapabilities.IsSupported(native, feature));
void SaveReport() => File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
    native, nativeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))).ToLowerInvariant(),
    model, modelSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model))).ToLowerInvariant(), hipPath,
    assemblyIdentity = typeof(MIGraphXProgram).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    gpuOptimizationLevel = Environment.GetEnvironmentVariable("MIGRAPHX_GPU_OPTIMIZE") ?? "3 (native default)",
    capabilities = capabilities.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value), group, activeCheck, failures, checks
}, new JsonSerializerOptions { WriteIndented = true }));
void Check(string name, Action action)
{
    var checkGroup = name.StartsWith("gpu-calibrated-dot-", StringComparison.Ordinal) ? name[19..]
        : name.StartsWith("gpu-custom-", StringComparison.Ordinal) ? "callbacks"
        : name.StartsWith("gpu-soak-", StringComparison.Ordinal) ? "soak" : "general";
    if (group != "all" && group != checkGroup) return;
    activeCheck = name;
    SaveReport();
    try { action(); checks.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception exception) { failures++; checks.Add(new { name, passed = false, error = exception.ToString() }); Console.WriteLine("FAIL " + name + ": " + exception); }
    activeCheck = null;
    SaveReport();
}
void Reject(Action action)
{
    try { action(); } catch (NotSupportedException) { return; }
    throw new InvalidOperationException("Unavailable feature was accepted.");
}
void Equal(float[] expected, float[] actual)
{
    if (expected.Length != actual.Length || expected.Where((value, i) => !float.IsFinite(actual[i]) || Math.Abs(value - actual[i]) > 1e-5f).Any())
        throw new InvalidOperationException("Unexpected inference values: " + string.Join(",", actual));
}
var values = new[] { 0.25f, -1f, 2f, 9f };
MIGraphXProgram Parse(bool dynamic = false, bool symbolic = false)
{
    using var options = new MIGraphXOnnxOptions(native);
    if (capabilities[MIGraphXFeature.OnnxDimensionParameters])
    {
        options.SetUseDebugSymbols(true);
        options.SetDimensionParameter("N", new MIGraphXDynamicDimension(1, 2));
    }
    if (dynamic)
    {
        var dimension = symbolic ? MIGraphXDynamicDimension.Symbolic("N", new Dictionary<string, MIGraphXDynamicDimension> { ["N"] = new(1, 2) }) : new MIGraphXDynamicDimension(1, 2);
        options.SetDynamicInputParameterShape("input", new[] { dimension, new MIGraphXDynamicDimension(4) });
    }
    using var clone = options.Clone();
    return MIGraphXProgram.ParseOnnxBuffer(File.ReadAllBytes(model), clone);
}
void Compile(MIGraphXProgram program, bool offload = true)
{
    using var target = capabilities[MIGraphXFeature.TargetOptions] ? new MIGraphXTarget(native, "gpu", "{}") : new MIGraphXTarget(native);
    using var targetClone = target.Clone();
    using var options = capabilities[MIGraphXFeature.AdvancedCompilation] ? new MIGraphXCompileOptions(native, offload, false, false, MIGraphXCompileMode.Eager, "{}") : new MIGraphXCompileOptions(native, offload);
    using var clone = options.Clone();
    if (clone.CompileMode != options.CompileMode || clone.BackendOptionsJson != options.BackendOptionsJson) throw new InvalidOperationException("Compile clone metadata changed.");
    program.Compile(targetClone, clone);
}
void Run(MIGraphXProgram program, bool trace = false)
{
    var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
    using var input = MIGraphXArgument.Create(native, shape, values);
    using var map = new MIGraphXParameterMap(native);
    map.Add("input", input);
    var snapshots = new List<MIGraphXTraceInfo>();
    using var result = trace ? program.RunTrace(map, snapshots.Add) : program.Run(map);
    Equal(values, result[0].ToArray<float>());
    if (trace)
    {
        if (snapshots.Count == 0 || snapshots.Any(info => string.IsNullOrEmpty(info.Name))) throw new InvalidOperationException("No valid trace snapshots.");
        var marker = new InvalidOperationException("observer marker");
        try { using var discarded = program.RunTrace(map, _ => throw marker); throw new Exception("Observer failure was lost."); }
        catch (InvalidOperationException exception) when (ReferenceEquals(marker, exception)) { }
        using var recovery = program.Run(map);
        Equal(values, recovery[0].ToArray<float>());
    }
}
Check("optional-export-capabilities", () =>
{
    foreach (var feature in Enum.GetValues<MIGraphXFeature>())
    {
        var missing = MIGraphXCapabilities.GetMissingExports(native, feature);
        Console.WriteLine(feature + ": " + (missing.Count == 0 ? "available" : string.Join(",", missing)));
    }
    if (!MIGraphXCapabilities.IsSupported(native, MIGraphXFeature.TensorFlow))
    {
        try { using var tf = new MIGraphXTfOptions(native); throw new Exception("Missing TF was accepted."); }
        catch (NotSupportedException) { }
    }
});
Check("unavailable-optional-apis-rejected", () =>
{
    using var options = new MIGraphXOnnxOptions(native);
    using var program = new MIGraphXProgram(native);
    using var map = new MIGraphXParameterMap(native);
    if (!capabilities[MIGraphXFeature.AdvancedCompilation]) Reject(() => { using var setting = new MIGraphXCompileOptions(native, true, false, false, MIGraphXCompileMode.Eager, "{}"); });
    if (!capabilities[MIGraphXFeature.TargetOptions]) Reject(() => { using var target = new MIGraphXTarget(native, "gpu", "{}"); });
    if (!capabilities[MIGraphXFeature.ExecutionTrace]) Reject(() => { using var result = program.RunTrace(map, _ => { }); });
    if (!capabilities[MIGraphXFeature.OnnxDimensionParameters]) { Reject(() => options.SetUseDebugSymbols(true)); Reject(() => options.SetDimensionParameter("N", new(1, 2))); }
    if (!capabilities[MIGraphXFeature.SymbolicDimensions]) Reject(() => options.SetDefaultDynamicDimensionValue(MIGraphXDynamicDimension.Symbolic("N", new Dictionary<string, MIGraphXDynamicDimension> { ["N"] = new(1, 2) })));
});
Check("gpu-onnx-file", () => Equal(values, MIGraphXOnnxWorkflow.RunFile(native, model, values).Output.ToArray()));
Check("gpu-buffer-options-clones", () => { using var program = Parse(); Compile(program); Run(program); });
if (capabilities[MIGraphXFeature.ExecutionTrace]) Check("gpu-trace-observer-recovery", () => { using var program = Parse(); Compile(program); Run(program, trace: true); });
Check("gpu-dynamic-range", () => { using var program = Parse(dynamic: true); Compile(program); Run(program); });
if (capabilities[MIGraphXFeature.SymbolicDimensions]) Check("gpu-symbolic-dimension", () => { using var program = Parse(dynamic: true, symbolic: true); Compile(program); Run(program); });
Check("gpu-save-load", () =>
{
    using var program = Parse();
    using var options = new MIGraphXFileOptions(native);
    var path = Path.Combine(output, "identity.mxr");
    program.Save(path, options);
    using var loaded = MIGraphXProgram.Load(path, options);
    Compile(loaded); Run(loaded);
});
Check("gpu-graph-two-inputs-two-outputs", () =>
{
    using var program = new MIGraphXProgram(native);
    using var main = program.GetMainModule();
    var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
    using var left = main.AddParameter("left", shape);
    using var right = main.AddParameter("right", shape);
    using var arguments = new MIGraphXInstructions(native, new[] { left, right });
    using var add = MIGraphXOperation.Create(native, "add");
    using var sum = main.AddInstruction(add, arguments);
    using var returns = new MIGraphXInstructions(native, new[] { sum, left });
    using var resultInstruction = main.AddReturn(returns);
    Compile(program);
    using var a = MIGraphXArgument.Create(native, shape, values);
    using var b = MIGraphXArgument.Create(native, shape, values);
    using var map = new MIGraphXParameterMap(native);
    map.Add("left", a); map.Add("right", b);
    using var result = program.Run(map);
    if (result.Count != 2) throw new InvalidOperationException("Expected two outputs.");
    Equal(values.Select(value => value * 2).ToArray(), result[0].ToArray<float>());
    Equal(values, result[1].ToArray<float>());
});
Check("gpu-fp16-transform", () => { using var program = Parse(); program.QuantizeFp16(); Compile(program); Run(program); });
Check("hip-host-and-device-async", () =>
{
    using var hip = new HipRuntime(hipPath);
    using var stream = hip.CreateStream();
    var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
    using (var program = Parse())
    {
        Compile(program);
        using var input = MIGraphXArgument.Create(native, shape, values);
        using var map = new MIGraphXParameterMap(native); map.Add("input", input);
        using var run = program.RunHostAsync(map, stream); run.Synchronize();
        Equal(values, run.Outputs[0].ToArray<float>());
    }
    using var deviceProgram = Parse(); Compile(deviceProgram, offload: false);
    var bytes = new byte[16]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
    using var deviceInput = hip.Allocate(16); deviceInput.CopyFrom(bytes);
    var inputs = new[] { new MIGraphXHipDeviceInput("input", shape, deviceInput) };
    using (var run = deviceProgram.RunDeviceAsync(inputs, stream)) { run.Synchronize(); Equal(values, run.Outputs[0].ToArray<float>()); }
    using var deviceOutput = hip.Allocate(16);
    using (var run = deviceProgram.RunDeviceIntoAsync(inputs, new[] { deviceOutput }, stream)) run.Synchronize();
    deviceOutput.CopyTo(bytes); var actual = new float[4]; Buffer.BlockCopy(bytes, 0, actual, 0, bytes.Length); Equal(values, actual);
});
RuntimeRegression.Run(native, hipPath, model, output, Check, Compile);
GpuCallbackRegression.Run(native, hipPath, output, Check, Compile);
SoakRegression.Run(native, hipPath, output, Check, Compile);
SaveReport();
return failures == 0 ? 0 : 1;
