using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using JYPPX.ROCm.MIGraphXSharp;

namespace MIGraphXSharp.Samples.PPStructure;

public sealed record ModelTensor(long[] Shape, Array Data)
{
    public float[] Floats => Data as float[] ?? throw new InvalidDataException("Expected float32 output.");
    public long[] Integers => Data switch { long[] x => x, int[] x => Array.ConvertAll(x, v => (long)v), _ => throw new InvalidDataException("Expected integer output.") };
}

/// <summary>Owns one reusable compiled GPU program. Calls are sequential.</summary>
public sealed class ModelSession : IDisposable
{
    private readonly MIGraphXProgram program;
    private readonly string native;
    private readonly IReadOnlyDictionary<string, MIGraphXShape> inputs;
    public string ModelSha256 { get; }
    public double ParseMilliseconds { get; }
    public double CompileMilliseconds { get; }
    public IReadOnlyDictionary<string, MIGraphXShape> Inputs => inputs;
    public ModelSession(string native, string model, IReadOnlyDictionary<string, long[]> dimensions)
    {
        this.native = Path.GetFullPath(native);
        ModelSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model))).ToLowerInvariant();
        using var options = new MIGraphXOnnxOptions(this.native);
        foreach (var (name, shape) in dimensions) options.SetInputParameterShape(name, shape);
        var watch = Stopwatch.StartNew();
        program = MIGraphXProgram.ParseOnnxFile(Path.GetFullPath(model), options);
        ParseMilliseconds = watch.Elapsed.TotalMilliseconds;
        try
        {
            inputs = program.GetParameterShapes();
            if (inputs.Keys.Except(dimensions.Keys).Any() || dimensions.Keys.Except(inputs.Keys).Any())
                throw new InvalidDataException("Model input names do not match the selected profile.");
            foreach (var (name, shape) in dimensions)
                if (!inputs[name].Lengths.SequenceEqual(shape)) throw new InvalidDataException("Unexpected input shape: " + name);
            using var target = new MIGraphXTarget(this.native, "gpu");
            using var compile = new MIGraphXCompileOptions(this.native, offloadCopy: true);
            watch.Restart(); program.Compile(target, compile);
            CompileMilliseconds = watch.Elapsed.TotalMilliseconds;
        }
        catch { program.Dispose(); throw; }
    }
    public ModelTensor[] Run(IReadOnlyDictionary<string, Array> values, string? dump = null)
    {
        if (values.Count != inputs.Count || inputs.Keys.Except(values.Keys).Any())
            throw new ArgumentException("Supply every named model input exactly once.");
        var arguments = new List<MIGraphXArgument>();
        try
        {
            using var parameters = new MIGraphXParameterMap(native);
            foreach (var (name, shape) in inputs)
            {
                var data = values[name];
                var argument = data switch
                {
                    float[] x => MIGraphXArgument.Create(native, shape, x),
                    long[] x => MIGraphXArgument.Create(native, shape, x),
                    int[] x => MIGraphXArgument.Create(native, shape, x),
                    _ => throw new ArgumentException("Only float32, int64 and int32 inputs are used by these profiles.")
                };
                arguments.Add(argument); parameters.Add(name, argument);
            }
            using var outputs = program.Run(parameters);
            var result = outputs.Select(x => new ModelTensor(x.Shape.Lengths.ToArray(), x.Shape.DataType switch
            {
                MIGraphXShapeDataType.Float32 => x.ToArray<float>(),
                MIGraphXShapeDataType.Int64 => x.ToArray<long>(),
                MIGraphXShapeDataType.Int32 => x.ToArray<int>(),
                _ => throw new InvalidDataException("Unsupported output element type: " + x.Shape.DataType)
            })).ToArray();
            if (result.Any(x => x.Data is float[] f && f.Any(v => !float.IsFinite(v))))
                throw new InvalidDataException("Model returned non-finite output.");
            if (dump is not null) Dump(dump, values, result);
            return result;
        }
        finally { foreach (var argument in arguments) argument.Dispose(); }
    }
    private void Dump(string directory, IReadOnlyDictionary<string, Array> values, ModelTensor[] outputs)
    {
        Directory.CreateDirectory(directory);
        var index = 0;
        foreach (var value in values.Values) Save(Path.Combine(directory, $"input-{index++}.bin"), value);
        for (var i = 0; i < outputs.Length; i++) Save(Path.Combine(directory, $"output-{i}.bin"), outputs[i].Data);
        JsonOutput.Write(Path.Combine(directory, "tensors.json"), new
        {
            modelSha256 = ModelSha256,
            inputs = values.Select((v, i) => new { index = i, name = v.Key, shape = inputs[v.Key].Lengths, type = v.Value.GetType().GetElementType()!.Name }),
            outputs = outputs.Select((v, i) => new { index = i, shape = v.Shape, type = v.Data.GetType().GetElementType()!.Name })
        });
    }
    private static void Save(string path, Array value)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException();
        var bytes = value switch
        {
            float[] x => MemoryMarshal.AsBytes(x.AsSpan()).ToArray(),
            long[] x => MemoryMarshal.AsBytes(x.AsSpan()).ToArray(),
            int[] x => MemoryMarshal.AsBytes(x.AsSpan()).ToArray(),
            _ => throw new InvalidDataException()
        };
        File.WriteAllBytes(path, bytes);
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
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }) + Environment.NewLine);
    }
}
public sealed class Arguments
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    public Arguments(string[] args)
    {
        for (var i = 0; i < args.Length; i += 2)
            if (i + 1 == args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException("Use unique --name value options.");
    }
    public bool Has(string name) => values.ContainsKey(name);
    public string Get(string name, string fallback) => values.GetValueOrDefault(name, fallback);
    public string Required(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Missing --" + name);
    public int Integer(string name, int fallback, int minimum = 1, int maximum = 10000)
    {
        var value = int.Parse(Get(name, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        return value >= minimum && value <= maximum ? value : throw new ArgumentOutOfRangeException(name);
    }
}
