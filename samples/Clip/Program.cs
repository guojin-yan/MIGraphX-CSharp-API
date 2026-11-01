using System.Diagnostics;
using System.Net;
using System.Text.Json;
using MIGraphXSharp.Samples.Clip;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

try
{
    var argsMap = new Arguments(args);
    var models = Path.GetFullPath(argsMap.Required("models")); var manifest = Path.GetFullPath(argsMap.Required("manifest"));
    var output = Path.GetFullPath(argsMap.Required("output"));
    var job = JsonSerializer.Deserialize<ClipJob>(File.ReadAllText(manifest), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Empty manifest.");
    if (job.Prompts is not { Length: > 0 and <= 64 } || job.Images is not { Length: > 0 and <= 1000 } || job.Prompts.Any(p => p is null || string.IsNullOrWhiteSpace(p.Key)) || job.Prompts.Select(p => p.Key).Distinct().Count() != job.Prompts.Length || job.Images.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Path)) || job.Images.Select(p => p.Id).Distinct().Count() != job.Images.Length)
        throw new InvalidDataException("Provide unique prompt keys, unique image IDs, 1..64 prompts and 1..1000 images.");
    var images = new List<Image<Rgb24>>();
    try
    {
        foreach (var image in job.Images)
        {
            var decoded = Image.Load<Rgb24>(Path.GetFullPath(image.Path, Path.GetDirectoryName(manifest)!));
            images.Add(decoded); decoded.Mutate(c => c.AutoOrient());
        }
        var batch = argsMap.Integer("batch", 4, maximum: 16); var warmup = argsMap.Integer("warmup", 1, minimum: 0, maximum: 1000); var iterations = argsMap.Integer("iterations", 10, maximum: 10000);
        var tokens = new ClipTokenizer(models).Encode(job.Prompts.Select(p => p.Text).ToArray());
        var native = argsMap.Required("native");
        var textClock = Stopwatch.StartNew();
        using var text = new ModelSession(native, Path.Combine(models, "clip-vit-b-32.text-encoder.onnx"), new Dictionary<string, long[]> { { "input_ids", [job.Prompts.Length, 77] }, { "attention_mask", [job.Prompts.Length, 77] } });
        var textInputs = new Dictionary<string, Array> { { "input_ids", tokens.Ids }, { "attention_mask", tokens.Mask } };
        var textRun = Stopwatch.StartNew(); var textFeatures = ClipProcessing.Normalize(text.Run(textInputs)[0].Floats, job.Prompts.Length); var textInferenceMilliseconds = textRun.Elapsed.TotalMilliseconds;
        var textInitializationMilliseconds = textClock.Elapsed.TotalMilliseconds;
        using var encoder = new ModelSession(native, Path.Combine(models, "clip-vit-b-32.image-encoder.onnx"), new Dictionary<string, long[]> { { "pixel_values", [batch, 3, 224, 224] } });
        ClipImageResult[] Run(string? dump = null)
        {
            var results = new List<ClipImageResult>();
            for (var first = 0; first < images.Count; first += batch)
            {
                var count = Math.Min(batch, images.Count - first); var input = new float[batch * 3 * 224 * 224];
                for (var slot = 0; slot < count; slot++) ClipProcessing.Prepare(images[first + slot]).CopyTo(input, slot * 3 * 224 * 224);
                var encoded = encoder.Run(new Dictionary<string, Array> { { "pixel_values", input } }, dump is null ? null : Path.Combine(dump, $"image-batch-{first / batch:D3}"))[0];
                if (!encoded.Shape.SequenceEqual(new long[] { batch, 512 })) throw new InvalidDataException("Image encoder must return [batch,512].");
                var rows = ClipProcessing.Normalize(encoded.Floats, batch);
                for (var slot = 0; slot < count; slot++) results.Add(new(job.Images[first + slot].Id, ClipProcessing.Rank(rows[slot], textFeatures, job.Prompts), rows[slot]));
            }
            return results.ToArray();
        }
        var samples = new List<double>(); ClipImageResult[] result = [];
        for (var i = 0; i < warmup + iterations; i++) { var watch = Stopwatch.StartNew(); result = Run(); if (i >= warmup) samples.Add(watch.Elapsed.TotalMilliseconds); }
        if (argsMap.Has("dump-tensors"))
        {
            text.Run(textInputs, Path.Combine(output, "text-tensors"));
            var captured = Run(output);
            if (!captured.Select(r => r.Matches[0].Key).SequenceEqual(result.Select(r => r.Matches[0].Key))) throw new InvalidDataException("Classification changed during capture.");
        }
        var imageLabels = job.Images.Select(i => new Prompt(i.Id, i.Path)).ToArray();
        var textQueries = job.Prompts.Select((p, i) => new { p.Key, p.Text, images = ClipProcessing.Rank(textFeatures[i], result.Select(r => r.Embedding).ToArray(), imageLabels) }).ToArray();
        samples.Sort(); var mean = samples.Average();
        JsonOutput.Write(Path.Combine(output, "result.json"), new
        {
            task = "clip",
            model = "openai/clip-vit-base-patch32",
            backend = "migraphx-gpu",
            batch,
            encoder.ModelSha256,
            textModelSha256 = text.ModelSha256,
            textInitializationMilliseconds,
            textInferenceMilliseconds,
            performance = new { warmupIterations = warmup, measuredIterations = iterations, unit = "milliseconds", batchPipeline = new { mean, p50 = samples[(int)Math.Ceiling(samples.Count * .5) - 1], p95 = samples[(int)Math.Ceiling(samples.Count * .95) - 1] }, imagesPerSecond = 1000 * images.Count / mean, samples },
            timingScope = "All decoded images: CPU resize/normalize, GPU model execution with host/device copies, CPU feature normalization/ranking. Excludes decode, text feature initialization, compilation and export.",
            results = result,
            textQueries
        });
        File.WriteAllText(Path.Combine(output, "index.html"), "<!doctype html><meta charset=utf-8><title>CLIP 图文检索</title><style>body{font:16px sans-serif;max-width:1000px;margin:auto;background:#0c1424;color:#eef}section{padding:20px;border-bottom:1px solid #456}img{max-width:280px;max-height:220px}</style><h1>CLIP 图文匹配</h1><p>分数为当前候选组内的 Softmax，不是业务概率。</p>" + string.Join("", result.Select((r, i) => $"<section><h2>{WebUtility.HtmlEncode(r.Id)}</h2><img src=\"data:image/png;base64,{Preview(images[i])}\"><ol>{string.Join("", r.Matches.Select(m => $"<li>{WebUtility.HtmlEncode(m.Text)} · cosine {m.Cosine:F4} · score {m.CandidateScore:F4}</li>"))}</ol></section>")));
        File.AppendAllText(Path.Combine(output, "index.html"), "<h1>文字检索图片</h1>" + string.Join("", textQueries.Select(q => $"<section><h2>{WebUtility.HtmlEncode(q.Text)}</h2><ol>{string.Join("", q.images.Select(m => $"<li>{WebUtility.HtmlEncode(m.Key)} · cosine {m.Cosine:F4} · score {m.CandidateScore:F4}</li>"))}</ol></section>")));
        foreach (var r in result) Console.WriteLine($"{r.Id}: {r.Matches[0].Key}, cosine={r.Matches[0].Cosine:F4}");
        Console.WriteLine($"All-image pipeline mean {mean:F2} ms; {1000 * images.Count / mean:F2} images/s.");
        return 0;
    }
    finally { foreach (var image in images) image.Dispose(); }
}
catch (Exception e) { Console.Error.WriteLine(e); Console.Error.WriteLine("Usage: --native /opt/rocm/lib/libmigraphx_c.so.3 --models /models/clip --manifest inputs.json --output /results/clip [--batch 4 --warmup 5 --iterations 30 --dump-tensors true]"); return 1; }

static string Preview(Image<Rgb24> image) { using var stream = new MemoryStream(); image.SaveAsPng(stream); return Convert.ToBase64String(stream.ToArray()); }
public sealed record ClipImage(string Id, string Path);
public sealed record ClipJob(Prompt[] Prompts, ClipImage[] Images);
public sealed record ClipImageResult(string Id, Match[] Matches, float[] Embedding);
