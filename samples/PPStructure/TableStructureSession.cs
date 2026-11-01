namespace MIGraphXSharp.Samples.PPStructure;

/// <summary>SLANeXt encoder + fixed-shape recurrent step, both executed by MIGraphX GPU.</summary>
public sealed class TableStructureSession : IDisposable
{
    private readonly ModelSession encoder, step;
    public object Information => new
    {
        encoder.ModelSha256,
        encoder.ParseMilliseconds,
        encoder.CompileMilliseconds,
        decoder = new { step.ModelSha256, step.ParseMilliseconds, step.CompileMilliseconds }
    };
    public TableStructureSession(string native, string directory, string style)
    {
        encoder = new ModelSession(native, Path.Combine(directory, $"slanext-{style}.encoder.onnx"), new Dictionary<string, long[]> { { "x", [1, 3, 512, 512] } });
        try { step = new ModelSession(native, Path.Combine(directory, $"slanext-{style}.step.onnx"), new Dictionary<string, long[]> { { "hidden", [1, 512] }, { "token", [1] }, { "memory0", [1, 256, 512] } }); }
        catch { encoder.Dispose(); throw; }
    }
    public ModelTensor Run(float[] image, string? dump = null)
    {
        var memory = encoder.Run(new Dictionary<string, Array> { { "x", image } }, dump is null ? null : Path.Combine(dump, "encoder")).Single();
        float[] hidden = new float[512]; int[] token = [0]; var probabilities = new List<float>();
        for (var iteration = 0; iteration < 501; iteration++)
        {
            var result = step.Run(new Dictionary<string, Array> { { "hidden", hidden }, { "token", token }, { "memory0", memory.Floats } },
                dump is null ? null : Path.Combine(dump, $"step-{iteration:D3}"));
            if (result.Length != 4 || result[0].Floats.Length != 512 || result[3].Floats.Length != 50) throw new InvalidDataException("Unexpected recurrent table outputs.");
            hidden = result[0].Floats; token = [checked((int)result[1].Integers.Single())];
            var logits = result[3].Floats; var maximum = logits.Max(); var weights = logits.Select(x => Math.Exp(x - maximum)).ToArray(); var sum = weights.Sum();
            probabilities.AddRange(weights.Select(x => (float)(x / sum)));
            if (token[0] == 49) break;
        }
        return new([1, probabilities.Count / 50, 50], probabilities.ToArray());
    }
    public void Dispose() { step.Dispose(); encoder.Dispose(); }
}
