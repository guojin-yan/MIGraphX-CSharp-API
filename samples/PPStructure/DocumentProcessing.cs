using System.Collections;
using System.Net;
using System.Text;
using Microsoft.ML.Tokenizers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using YamlDotNet.Serialization;
using PointD = MIGraphXSharp.Samples.PPStructure.Ocr.PointD;

namespace MIGraphXSharp.Samples.PPStructure;

public sealed record Box(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
    public PointD[] Polygon => [new(X, Y), new(X + Width, Y), new(X + Width, Y + Height), new(X, Y + Height)];
    public Rectangle Rectangle(int width, int height)
    {
        var left = Math.Clamp((int)Math.Floor(X), 0, width - 1); var top = Math.Clamp((int)Math.Floor(Y), 0, height - 1);
        return new Rectangle(left, top, Math.Clamp((int)Math.Ceiling(X + Width) - left, 1, width - left), Math.Clamp((int)Math.Ceiling(Y + Height) - top, 1, height - top));
    }
}
public sealed record Region(string Category, float Score, Box Bounds);
public static class DocumentProcessing
{
    public static readonly string[] Labels = "paragraph_title,image,text,number,abstract,content,figure_title,formula,table,table_title,reference,doc_title,footnote,header,algorithm,footer,seal,chart_title,chart,formula_number,header_image,footer_image,aside_text".Split(',');
    public static float[] Tensor(Image<Rgb24> image, int width, int height, bool imagenet = false, bool bgr = false, bool pad = false, bool classification = false)
    {
        Image<Rgb24> prepared;
        if (classification)
        {
            var ratio = 256d / Math.Min(image.Width, image.Height); var w = Math.Max(224, (int)(image.Width * ratio)); var h = Math.Max(224, (int)(image.Height * ratio));
            prepared = image.Clone(c => c.Resize(w, h, KnownResamplers.Triangle).Crop(new Rectangle((w - 224) / 2, (h - 224) / 2, 224, 224)));
        }
        else if (pad)
        {
            var ratio = Math.Min((double)width / image.Width, (double)height / image.Height);
            var w = Math.Clamp((int)(image.Width * ratio), 1, width); var h = Math.Clamp((int)(image.Height * ratio), 1, height);
            using var resized = image.Clone(c => c.Resize(w, h, KnownResamplers.Triangle));
            prepared = new Image<Rgb24>(width, height); prepared.Mutate(c => c.DrawImage(resized, new Point(0, 0), 1));
            var values = Ocr.Images.ToTensor(prepared, !bgr, imagenet ? [.485f, .456f, .406f] : null, imagenet ? [.229f, .224f, .225f] : null);
            // Paddle pads after normalization, so the padded tensor is zero.
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) if (y >= h || x >= w) for (var ch = 0; ch < 3; ch++) values[ch * width * height + y * width + x] = 0;
            prepared.Dispose(); return values;
        }
        else prepared = image.Clone(c => c.Resize(width, height, KnownResamplers.Bicubic));
        using (prepared) return Ocr.Images.ToTensor(prepared, !bgr, imagenet ? [.485f, .456f, .406f] : null, imagenet ? [.229f, .224f, .225f] : null);
    }
    public static Region[] DecodeRegions(ModelTensor[] tensors, string[] labels, int width, int height, float threshold)
    {
        if (tensors.Length < 1 || tensors[0].Shape.Length != 2 || tensors[0].Shape[1] != 6) throw new InvalidDataException("Expected Paddle [count,6] regions.");
        var data = tensors[0].Floats; var count = data.Length / 6;
        if (tensors.Length > 1) { var counts = tensors[1].Integers; if (counts.Length != 1 || counts[0] < 0 || counts[0] > count) throw new InvalidDataException("Invalid Paddle region count."); count = (int)counts[0]; }
        var result = new List<Region>();
        for (var i = 0; i < count; i++)
        {
            var k = i * 6; var cls = (int)data[k]; var score = data[k + 1];
            if (cls < 0 || score < threshold) continue;
            if (cls >= labels.Length || data[k] != cls || score > 1) throw new InvalidDataException("Invalid region class or probability.");
            var x1 = Math.Clamp(data[k + 2] / 640d * width, 0, width); var y1 = Math.Clamp(data[k + 3] / 640d * height, 0, height);
            var x2 = Math.Clamp(data[k + 4] / 640d * width, 0, width); var y2 = Math.Clamp(data[k + 5] / 640d * height, 0, height);
            if (x2 > x1 && y2 > y1) result.Add(new(labels[cls], score, new(x1, y1, x2 - x1, y2 - y1)));
        }
        return result.OrderBy(r => r.Bounds.Y).ThenBy(r => r.Bounds.X).ToArray();
    }
    public static PointD ToSource(PointD p, int width, int height, int rotation) => rotation switch
    { 90 => new(width - p.Y, p.X), 180 => new(width - p.X, height - p.Y), 270 => new(p.Y, height - p.X), 0 => p, _ => throw new ArgumentOutOfRangeException(nameof(rotation)) };
    public static (int Index, float Confidence) Classification(ModelTensor output, int classes)
    {
        var values = output.Floats; if (values.Length != classes || values.Any(v => v < 0 || v > 1)) throw new InvalidDataException("Expected classification probabilities.");
        var i = Array.IndexOf(values, values.Max()); return (i, values[i]);
    }
    public static string[] TableTokens()
    {
        var tokens = new List<string> { "<SOS>", "<thead>", "</thead>", "<tbody>", "</tbody>", "<tr>", "</tr>", "<td", ">", "</td>" };
        tokens.AddRange(Enumerable.Range(2, 19).Select(i => $" colspan=\"{i}\"")); tokens.AddRange(Enumerable.Range(2, 19).Select(i => $" rowspan=\"{i}\""));
        tokens.Add("<td></td>"); tokens.Add("<EOS>"); return tokens.ToArray();
    }
    public static (string Markup, int Cells, bool Complete) DecodeTable(ModelTensor scores)
    {
        if (scores.Shape.Length != 3 || scores.Shape[0] != 1 || scores.Shape[2] != 50) throw new InvalidDataException("Expected SLANeXt [1,steps,50].");
        var tokens = TableTokens(); var text = new StringBuilder(); var cells = 0; var complete = false;
        for (var step = 0; step < scores.Shape[1]; step++)
        {
            var selected = 0; for (var c = 1; c < 50; c++) if (scores.Floats[step * 50 + c] > scores.Floats[step * 50 + selected]) selected = c;
            if (selected == 49) { complete = true; break; }
            if (selected == 0) continue;
            var token = tokens[selected]; if (token is "<td" or "<td></td>") cells++; text.Append(token);
        }
        return ("<table>" + text + "</table>", cells, complete);
    }
    public static string FillTable(string markup, IReadOnlyList<string> texts)
    {
        const string pattern = "<td(?:\\s+[^>]*)?>";
        if (System.Text.RegularExpressions.Regex.Matches(markup, pattern).Count != texts.Count) throw new ArgumentException("Provide one text for every table cell.");
        var index = 0; return System.Text.RegularExpressions.Regex.Replace(markup, pattern, m => m.Value + WebUtility.HtmlEncode(texts[index++]));
    }
}

public sealed class FormulaTokenizer
{
    private readonly BpeTokenizer tokenizer;
    public FormulaTokenizer(string yamlPath)
    {
        var root = (IDictionary)new DeserializerBuilder().Build().Deserialize<object>(File.ReadAllText(yamlPath));
        static IDictionary Get(IDictionary map, string key) => (IDictionary)(map[key] ?? throw new InvalidDataException("Missing formula tokenizer " + key));
        var fast = Get(Get(Get(root, "PostProcess"), "character_dict"), "fast_tokenizer_file"); var model = Get(fast, "model");
        var vocabularyMap = Get(model, "vocab");
        var vocabulary = vocabularyMap.Keys.Cast<object>().Select(key => new KeyValuePair<string, int>((string)key, Convert.ToInt32(vocabularyMap[key]))).ToArray();
        var merges = ((IEnumerable)model["merges"]!).Cast<object>().Select(x => x.ToString()!).ToArray();
        var specials = new Dictionary<string, int>(); foreach (IDictionary added in (IEnumerable)fast["added_tokens"]!) specials.Add((string)added["content"]!, Convert.ToInt32(added["id"]));
        tokenizer = BpeTokenizer.Create(new BpeOptions(vocabulary) { Merges = merges, ByteLevel = true, SpecialTokens = specials });
    }
    public (string Latex, bool Complete) Decode(long[] values)
    {
        var ids = new List<int>(); var complete = false;
        foreach (var id in values) { if (id == 2) { complete = true; break; } if (id is 0 or 1) continue; if (id < 0 || id > int.MaxValue) throw new InvalidDataException("Invalid formula token."); ids.Add((int)id); }
        return (tokenizer.Decode(ids), complete);
    }
    public static float[] Prepare(Image<Rgb24> source)
    {
        int min = 255, max = 0; int Gray(Rgb24 p) => (19595 * p.R + 38470 * p.G + 7471 * p.B + 32768) >> 16;
        source.ProcessPixelRows(rows => { for (var y = 0; y < source.Height; y++) foreach (var p in rows.GetRowSpan(y)) { var gray = Gray(p); min = Math.Min(min, gray); max = Math.Max(max, gray); } });
        int left = 0, top = 0, right = source.Width, bottom = source.Height;
        if (min != max)
        {
            left = source.Width; top = source.Height; right = bottom = 0;
            source.ProcessPixelRows(rows => { for (var y = 0; y < source.Height; y++) { var row = rows.GetRowSpan(y); for (var x = 0; x < source.Width; x++) if ((Gray(row[x]) - min) * 255 < 200 * (max - min)) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); } } });
        }
        var width = right - left; var height = bottom - top; var scale = 384d / Math.Min(width, height);
        var firstW = (int)(width * scale); var firstH = (int)(height * scale);
        if ((long)firstW * firstH > 80_000_000) throw new InvalidDataException("Formula resize is too large.");
        using var crop = source.Clone(c => c.Crop(new Rectangle(left, top, width, height)).Resize(firstW, firstH, KnownResamplers.Triangle));
        var shrink = Math.Min(1d, 384d / Math.Max(firstW, firstH)); var w = Math.Max(1, (int)Math.Round(firstW * shrink)); var h = Math.Max(1, (int)Math.Round(firstH * shrink));
        using var final = crop.Clone(c => c.Resize(w, h, KnownResamplers.Bicubic)); var values = Enumerable.Repeat(-.7931f / .1738f, 384 * 384).ToArray();
        final.ProcessPixelRows(rows => { for (var y = 0; y < h; y++) { var row = rows.GetRowSpan(y); for (var x = 0; x < w; x++) { var p = row[x]; values[(y + (384 - h) / 2) * 384 + x + (384 - w) / 2] = ((.114f * p.R + .587f * p.G + .299f * p.B) / 255f - .7931f) / .1738f; } } });
        return values;
    }
}
