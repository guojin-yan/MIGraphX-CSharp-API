using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MIGraphXSharp.Samples.PPStructure;

/// <summary>Explicit opt-in CPU formula execution; never reported as MIGraphX GPU inference.</summary>
public sealed class FormulaSession : IDisposable
{
    private readonly InferenceSession session;
    public string ModelSha256 { get; }
    public double InitializationMilliseconds { get; }
    public FormulaSession(string model)
    {
        ModelSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model))).ToLowerInvariant();
        var clock = Stopwatch.StartNew();
        using var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1 };
        session = new InferenceSession(model, options);
        InitializationMilliseconds = clock.Elapsed.TotalMilliseconds;
    }
    public long[] Run(float[] tensor)
    {
        using var outputs = session.Run([NamedOnnxValue.CreateFromTensor("x", new DenseTensor<float>(tensor, [1, 1, 384, 384]))]);
        return outputs.Single().AsTensor<long>().ToArray();
    }
    public void Dispose() => session.Dispose();
}
