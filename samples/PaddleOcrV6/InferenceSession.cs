using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using JYPPX.ROCm.MIGraphXSharp;

namespace MIGraphXSharp.Samples.PaddleOcrV6;

public sealed record Tensor(long[] Shape, float[] Data);

/// <summary>A single, explicitly sized float32 input and copied host outputs.</summary>
public sealed class InferenceSession : IDisposable
{
    private readonly MIGraphXProgram program;
    private readonly string native;
    private readonly string inputName;
    private readonly MIGraphXShape inputShape;
    public double ParseMilliseconds { get; }
    public double CompileMilliseconds { get; }
    public string ModelSha256 { get; }
    public string AssemblyIdentity { get; } = typeof(MIGraphXBuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public long[][] OutputShapes => program.GetOutputShapes().Select(x => x.Lengths.ToArray()).ToArray();

    public InferenceSession(string native, string model, string inputName, int width, int height)
    {
        this.native = Path.GetFullPath(native);
        this.inputName = inputName;
        model = Path.GetFullPath(model);
        ModelSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model))).ToLowerInvariant();
        inputShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 3, height, width });
        using var options = new MIGraphXOnnxOptions(this.native);
        options.SetInputParameterShape(inputName, inputShape.Lengths);
        var timer = Stopwatch.StartNew();
        program = MIGraphXProgram.ParseOnnxFile(model, options);
        ParseMilliseconds = timer.Elapsed.TotalMilliseconds;
        try
        {
            var shapes = program.GetParameterShapes();
            if (shapes.Count != 1 || !shapes.TryGetValue(inputName, out var actual) || !actual.HasSameNativeContent(inputShape))
                throw new InvalidOperationException("This sample requires exactly one float32 NCHW input matching the selected dimensions.");
            using var target = new MIGraphXTarget(this.native, "gpu");
            using var compile = new MIGraphXCompileOptions(this.native, offloadCopy: true);
            timer.Restart();
            program.Compile(target, compile);
            CompileMilliseconds = timer.Elapsed.TotalMilliseconds;
        }
        catch { program.Dispose(); throw; }
    }

    public (Tensor[] Outputs, double Milliseconds) Run(float[] input, string? dumpDirectory = null)
    {
        using var argument = MIGraphXArgument.Create(native, inputShape, input);
        using var parameters = new MIGraphXParameterMap(native);
        parameters.Add(inputName, argument);
        var timer = Stopwatch.StartNew();
        using var outputs = program.Run(parameters);
        var result = outputs.Select(value => new Tensor(value.Shape.Lengths.ToArray(), value.ToArray<float>())).ToArray();
        var elapsed = timer.Elapsed.TotalMilliseconds;
        if (result.Any(tensor => tensor.Data.Any(value => !float.IsFinite(value))))
            throw new InvalidOperationException("The model returned non-finite float32 output.");
        if (dumpDirectory is not null)
        {
            Dump(input, result, dumpDirectory);
        }
        return (result, elapsed);
    }

    public void Dump(float[] input, Tensor[] outputs, string directory)
    {
        Directory.CreateDirectory(directory);
        SaveFloats(Path.Combine(directory, "input.f32"), input);
        for (var i = 0; i < outputs.Length; ++i) SaveFloats(Path.Combine(directory, $"output-{i}.f32"), outputs[i].Data);
        JsonOutput.Write(Path.Combine(directory, "tensors.json"), new
        {
            inputName, inputShape = inputShape.Lengths, outputs = outputs.Select((tensor, index) => new { index, tensor.Shape }),
            modelSha256 = ModelSha256, assemblyIdentity = AssemblyIdentity,
        });
    }

    private static void SaveFloats(string path, float[] values)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Tensor dumps require little-endian float32.");
        using var file = File.Create(path);
        file.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()));
    }

    public void Dispose() => program.Dispose();
}

public static class JsonOutput
{
    public static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + Environment.NewLine);
    }
}

public sealed class Arguments
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    public Arguments(string[] args)
    {
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 == args.Length || !args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index][2..], args[index + 1]))
                throw new ArgumentException("Use unique --name value options.");
        }
    }
    public string Required(string name) => values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Missing --{name}.");
    public string Get(string name, string fallback) => values.TryGetValue(name, out var value) ? value : fallback;
    public bool Has(string name) => values.ContainsKey(name);
    public int Integer(string name, int fallback, int minimum = 1, int maximum = 8192)
    {
        var value = int.Parse(Get(name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        return value >= minimum && value <= maximum ? value : throw new ArgumentOutOfRangeException(name);
    }
    public float Probability(string name, float fallback)
    {
        var value = float.Parse(Get(name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        return float.IsFinite(value) && value >= 0 && value <= 1 ? value : throw new ArgumentOutOfRangeException(name);
    }
}
