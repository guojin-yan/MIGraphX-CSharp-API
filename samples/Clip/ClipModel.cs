using System.Net;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MIGraphXSharp.Samples.Clip;

public sealed record Prompt(string Key, string Text);
public sealed record Match(string Key, string Text, double Cosine, double CandidateScore);

/// <summary>OpenAI CLIP ViT-B/32 English byte-level BPE with explicit length/range validation.</summary>
public sealed class ClipTokenizer
{
    private readonly BpeTokenizer tokenizer;
    public ClipTokenizer(string directory)
    {
        tokenizer = BpeTokenizer.Create(new BpeOptions(Path.Combine(directory, "clip-vit-b-32.vocab.json"), Path.Combine(directory, "clip-vit-b-32.merges.txt"))
        {
            ByteLevel = true,
            EndOfWordSuffix = "</w>",
            UnknownToken = "<|endoftext|>",
            PreTokenizer = new RegexPreTokenizer(new Regex("'s|'t|'re|'ve|'m|'ll|'d|[a-z]+|[0-9]|[^\\s\\p{L}\\p{N}]+", RegexOptions.CultureInvariant), null)
        });
    }
    public (long[] Ids, long[] Mask) Encode(IReadOnlyList<string> texts)
    {
        if (texts.Count is < 1 or > 64) throw new ArgumentException("Provide 1..64 English descriptions.");
        var ids = Enumerable.Repeat(49407L, texts.Count * 77).ToArray(); var mask = new long[ids.Length];
        for (var row = 0; row < texts.Count; row++)
        {
            static bool Invalid(string s) => string.IsNullOrWhiteSpace(s) || s.Any(c => c < 32 || c > 126 || c is '<' or '>');
            if (Invalid(texts[row])) throw new ArgumentException("CLIP sample prompts must be printable ASCII English without special tokens.");
            var clean = Regex.Replace(WebUtility.HtmlDecode(WebUtility.HtmlDecode(texts[row])), "\\s+", " ").Trim().ToLowerInvariant();
            if (Invalid(clean)) throw new ArgumentException("Prompt normalization produced unsupported characters.");
            var tokens = tokenizer.EncodeToIds(clean);
            if (tokens.Count > 75) throw new ArgumentException("Prompt exceeds 77 tokens including BOS/EOS; shorten it explicitly.");
            var offset = row * 77; ids[offset] = 49406; mask[offset] = 1;
            for (var i = 0; i < tokens.Count; i++) { ids[offset + i + 1] = tokens[i]; mask[offset + i + 1] = 1; }
            mask[offset + tokens.Count + 1] = 1;
        }
        return (ids, mask);
    }
}

public static class ClipProcessing
{
    public static float[] Prepare(Image<Rgb24> source)
    {
        // Resize the short edge first, then crop. Cropping the source first changes sampling.
        var scale = 224d / Math.Min(source.Width, source.Height);
        var w = Math.Max(224, (int)(source.Width * scale)); var h = Math.Max(224, (int)(source.Height * scale));
        using var resized = source.Clone(c => c.Resize(w, h, KnownResamplers.Bicubic).Crop(new Rectangle((w - 224) / 2, (h - 224) / 2, 224, 224)));
        float[] mean = [.48145466f, .4578275f, .40821073f], std = [.26862954f, .26130258f, .27577711f];
        var tensor = new float[3 * 224 * 224];
        resized.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < 224; y++)
            {
                var row = rows.GetRowSpan(y); for (var x = 0; x < 224; x++)
                { var p = row[x]; var k = y * 224 + x; tensor[k] = (p.R / 255f - mean[0]) / std[0]; tensor[224 * 224 + k] = (p.G / 255f - mean[1]) / std[1]; tensor[2 * 224 * 224 + k] = (p.B / 255f - mean[2]) / std[2]; }
            }
        });
        return tensor;
    }
    public static float[][] Normalize(float[] values, int rows)
    {
        if (rows < 1 || values.Length != rows * 512 || values.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Expected finite CLIP [batch,512] features.");
        var result = new float[rows][];
        for (var row = 0; row < rows; row++)
        {
            double norm = 0; for (var d = 0; d < 512; d++) norm += (double)values[row * 512 + d] * values[row * 512 + d];
            if (norm < 1e-20) throw new InvalidDataException("CLIP emitted a zero embedding.");
            result[row] = new float[512]; for (var d = 0; d < 512; d++) result[row][d] = (float)(values[row * 512 + d] / Math.Sqrt(norm));
        }
        return result;
    }
    public static Match[] Rank(float[] query, float[][] candidates, Prompt[] labels, double logitScale = 100.0000076)
    {
        if (query.Length != 512 || candidates.Length != labels.Length || labels.Length == 0 || candidates.Any(x => x.Length != 512) || !double.IsFinite(logitScale) || logitScale <= 0)
            throw new ArgumentException("Feature dimensions and labels must match.");
        var cosine = candidates.Select(v => Enumerable.Range(0, 512).Sum(i => (double)v[i] * query[i])).ToArray();
        var max = cosine.Max() * logitScale; var weights = cosine.Select(v => Math.Exp(v * logitScale - max)).ToArray(); var total = weights.Sum();
        return labels.Select((p, i) => new Match(p.Key, p.Text, cosine[i], weights[i] / total)).OrderByDescending(m => m.Cosine).ToArray();
    }
}
