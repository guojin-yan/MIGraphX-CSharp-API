using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using MIGraphXSharp.Samples.PPStructure;
using MIGraphXSharp.Samples.PPStructure.Ocr;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Arguments = MIGraphXSharp.Samples.PPStructure.Arguments;
using JsonOutput = MIGraphXSharp.Samples.PPStructure.JsonOutput;

try
{
    var options = new Arguments(args);
    var manifest = Path.GetFullPath(options.Required("manifest")); var output = Path.GetFullPath(options.Required("output"));
    var pages = JsonSerializer.Deserialize<DocumentInput[]>(File.ReadAllText(manifest), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Empty manifest.");
    if (pages.Length is < 1 or > 100 || pages.Any(p => p is null || !System.Text.RegularExpressions.Regex.IsMatch(p.Id ?? "", "^[a-z0-9][a-z0-9-]{0,63}$") || string.IsNullOrWhiteSpace(p.Path) || p.Kind is not ("auto" or "text" or "table" or "formula")) || pages.Select(p => p.Id).Distinct().Count() != pages.Length) throw new InvalidDataException("Provide 1..100 pages, unique lowercase IDs and kind auto|text|table|formula.");
    var warmup = options.Integer("warmup", 1, minimum: 1, maximum: 1000); var iterations = options.Integer("iterations", 5, maximum: 10000);
    Directory.CreateDirectory(output);
    using var pipeline = new DocumentPipeline(options.Required("native"), options.Required("models"), args);
    var summaries = new List<object>(); var html = new StringBuilder("<!doctype html><meta charset=utf-8><title>PP-Structure 文档处理</title><style>body{font:16px sans-serif;max-width:1200px;margin:auto;background:#101829;color:#eef}section{padding:24px;border-bottom:1px solid #456}img{max-width:100%;max-height:900px}td{border:1px solid #789;padding:6px}a{color:#7ef}pre{white-space:pre-wrap}</style><h1>PP-Structure 文档处理</h1>");
    int failed = 0, review = 0;
    foreach (var page in pages)
    {
        var directory = Path.Combine(output, page.Id); Directory.CreateDirectory(directory);
        try
        {
            using var source = Images.Load(Path.GetFullPath(page.Path, Path.GetDirectoryName(manifest)!));
            var initialization = Stopwatch.StartNew();
            for (var i = 0; i < warmup; i++) pipeline.Run(page, source);
            var initializationMilliseconds = initialization.Elapsed.TotalMilliseconds;
            var samples = new List<double>(); DocumentPage? result = null;
            for (var i = 0; i < iterations; i++) { var clock = Stopwatch.StartNew(); result = pipeline.Run(page, source); samples.Add(clock.Elapsed.TotalMilliseconds); }
            if (options.Has("dump-tensors")) result = pipeline.Run(page, source, Path.Combine(directory, "tensors"));
            var value = result!; samples.Sort();
            var performance = new { unit = "milliseconds", initializationAndWarmupMilliseconds = initializationMilliseconds, measuredIterations = iterations, mean = samples.Average(), p50 = samples[(int)Math.Ceiling(iterations * .5) - 1], p95 = samples[(int)Math.Ceiling(iterations * .95) - 1], samples };
            JsonOutput.Write(Path.Combine(directory, "page.json"), new
            {
                result = value,
                performance,
                models = pipeline.ModelInformation,
                ocr = pipeline.OcrInformation,
                timingScope = "Decoded page through orientation, CPU document preprocessing, MIGraphX GPU models, HIP OCR, CPU structure/text association and decoding, plus optional explicitly selected ORT CPU formula. Excludes compilation/warmup, decode, drawing, tensor export and file I/O. StageMilliseconds measures model Run with host copies and OCR wall time; preprocessing is included only in overall page latency."
            });
            var markdown = new StringBuilder("# " + page.Id + "\n\n");
            source.SaveAsPng(Path.Combine(directory, "source.png"));
            using var upright = source.Clone(c => c.Rotate(-value.CorrectionCounterClockwise));
            using var preview = new Visualization(upright);
            foreach (var region in value.Regions) preview.Polygon(region.Layout.Bounds.Polygon, region.Layout.Category, "#ff9b35");
            foreach (var line in value.Lines) preview.Polygon(line.Polygon, line.Text, "#35e5aa");
            preview.Save(directory);
            for (var i = 0; i < value.Regions.Length; i++)
            {
                var region = value.Regions[i]; markdown.AppendLine("## " + region.Layout.Category).AppendLine();
                if (region.Table is { } table)
                {
                    if (table.FilledHtml is not null) File.WriteAllText(Path.Combine(directory, $"table-{i:D2}.html"), "<!doctype html><meta charset=utf-8>" + table.FilledHtml);
                    JsonOutput.Write(Path.Combine(directory, $"table-{i:D2}.json"), table);
                    markdown.AppendLine(table.FilledHtml ?? "表格需复核：" + string.Join(", ", table.Diagnostics)).AppendLine();
                }
                if (region.Formula is { } formula) { File.WriteAllText(Path.Combine(directory, $"formula-{i:D2}.tex"), formula.Latex); markdown.AppendLine("$$\n" + formula.Latex + "\n$$\n"); }
                foreach (var line in region.Lines) markdown.AppendLine(line.Text + "\n");
            }
            File.WriteAllText(Path.Combine(directory, "document.md"), markdown.ToString());
            File.WriteAllLines(Path.Combine(directory, "text.txt"), value.Lines.Select(l => l.Text));
            html.Append("<section><h2>").Append(WebUtility.HtmlEncode(page.Id)).Append("</h2><p>").Append(WebUtility.HtmlEncode(string.Join(", ", value.Diagnostics))).Append("</p><a href=\"").Append(page.Id).Append("/page.json\">JSON</a> · <a href=\"").Append(page.Id).Append("/document.md\">Markdown</a><br><img src=\"").Append(page.Id).Append("/annotated.svg\"><pre>").Append(WebUtility.HtmlEncode(string.Join("\n", value.Lines.Select(l => l.Text)))).Append("</pre>");
            foreach (var r in value.Regions) { if (r.Table?.FilledHtml is { } table) html.Append(table); if (r.Formula is { } formula) html.Append("<pre>").Append(WebUtility.HtmlEncode(formula.Latex)).Append("</pre>"); }
            html.Append("</section>");
            summaries.Add(new { page.Id, status = value.Diagnostics.Length == 0 ? "complete" : "needs-review", regions = value.Regions.Length, lines = value.Lines.Length, performance });
            if (value.Diagnostics.Length > 0) review++;
            Console.WriteLine($"{page.Id}: {value.Regions.Length} regions, {value.Lines.Length} lines; mean {performance.mean:F2} ms, P95 {performance.p95:F2} ms.");
        }
        catch (Exception e) { failed++; JsonOutput.Write(Path.Combine(directory, "error.json"), new { page.Id, error = e.ToString() }); summaries.Add(new { page.Id, status = "failed", error = e.Message }); Console.Error.WriteLine($"{page.Id}: {e}"); }
    }
    File.WriteAllText(Path.Combine(output, "index.html"), html.ToString());
    JsonOutput.Write(Path.Combine(output, "summary.json"), new { backend = "migraphx-gpu+hip", formulaBackend = options.Get("formula-backend", "disabled"), pages = summaries, failed, needsReview = review });
    return failed > 0 ? 1 : review > 0 ? 3 : 0;
}
catch (Exception e) { Console.Error.WriteLine(e); Console.Error.WriteLine("Usage: --native /opt/rocm/lib/libmigraphx_c.so.3 --gpu-architecture gfx1100 --models /models/document --manifest documents.json --output /results/document [--rec-batch 4 --rec-workers 2 --warmup 1 --iterations 10 --dump-tensors true]"); return 1; }
