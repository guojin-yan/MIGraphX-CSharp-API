using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PointD = MIGraphXSharp.Samples.PPStructure.Ocr.PointD;

namespace MIGraphXSharp.Samples.PPStructure;

public sealed record DocumentInput(string Id, string Path, string Kind = "auto", bool AutoOrient = true);
public sealed record DocumentLine(string Text, float Confidence, PointD[] Polygon, PointD[] SourcePolygon);
public sealed record DocumentTable(string Style, string Structure, string? FilledHtml, Region[] Cells, string[] Diagnostics);
public sealed record DocumentFormula(string Backend, string Latex, bool Complete);
public sealed record DocumentRegion(Region Layout, PointD[] SourcePolygon, DocumentLine[] Lines, DocumentTable? Table, DocumentFormula? Formula);
public sealed record DocumentPage(string Id, int Width, int Height, int CorrectionCounterClockwise, float? OrientationConfidence,
    DocumentRegion[] Regions, DocumentLine[] Lines, string[] Diagnostics, IReadOnlyDictionary<string, double> StageMilliseconds);

/// <summary>Independent sample: reusable MIGraphX GPU models and one current-size HIP OCR pipeline.</summary>
public sealed class DocumentPipeline : IDisposable
{
    private readonly string native, models;
    private readonly Ocr.Arguments ocrOptions;
    private readonly Dictionary<string, ModelSession> sessions = [];
    private readonly Dictionary<string, TableStructureSession> tables = [];
    private Ocr.HipOcrPipeline? ocr;
    private (int Width, int Height) ocrSize;
    private FormulaTokenizer? formulaTokenizer;
    private FormulaSession? formulaSession;
    private readonly string formulaBackend;
    public DocumentPipeline(string native, string models, string[] args)
    {
        this.native = native; this.models = Path.GetFullPath(models); ocrOptions = new Ocr.Arguments(args);
        formulaBackend = ocrOptions.Get("formula-backend", "disabled");
        if (formulaBackend is not ("disabled" or "ort-cpu")) throw new ArgumentException("--formula-backend disabled|ort-cpu. FormulaNet is not supported by MIGraphX 2.15.");
    }
    public object ModelInformation => new { feedForward = sessions.Select(s => new { name = s.Key, s.Value.ModelSha256, s.Value.ParseMilliseconds, s.Value.CompileMilliseconds }).ToArray(), tables = tables.Select(t => new { style = t.Key, models = t.Value.Information }).ToArray(), formula = formulaSession is null ? null : new { backend = "ort-cpu", formulaSession.ModelSha256, formulaSession.InitializationMilliseconds } };
    public object? OcrInformation => ocr is null ? null : new { detector = ocr.DetectorInfo, recognizer = ocr.RecognizerInfo };

    private ModelSession Session(string name, string input, long[] dimensions)
    {
        if (!sessions.TryGetValue(name, out var model))
        {
            var inputs = new Dictionary<string, long[]> { { input, dimensions } };
            if (input == "image") { inputs.Add("im_shape", [1, 2]); inputs.Add("scale_factor", [1, 2]); }
            // Graph preparation is explicit and reproducible; never silently switch execution backends.
            var suffix = input == "image" || name.StartsWith("slanext", StringComparison.Ordinal) || name.StartsWith("pp-formulanet", StringComparison.Ordinal) ? ".migraphx.onnx" : ".onnx";
            Console.WriteLine("Initializing GPU model: " + name);
            model = new ModelSession(native, Path.Combine(models, name + suffix), inputs);
            sessions.Add(name, model);
        }
        return model;
    }

    public DocumentPage Run(DocumentInput page, Image<Rgb24> source, string? dump = null)
    {
        var stages = new Dictionary<string, double>(); var warnings = new List<string>();
        T Time<T>(string name, Func<T> action)
        { var clock = Stopwatch.StartNew(); var value = action(); stages[name] = stages.GetValueOrDefault(name) + clock.Elapsed.TotalMilliseconds; return value; }
        ModelTensor[] Execute(string name, string input, long[] dims, float[] values)
        {
            var model = Session(name, input, dims);
            var inputs = new Dictionary<string, Array> { { input, values } };
            if (input == "image") { inputs.Add("im_shape", new float[] { 640, 640 }); inputs.Add("scale_factor", new float[] { 1, 1 }); }
            return Time(name, () => model.Run(inputs, dump is null ? null : Path.Combine(dump, name + "-" + stages.Count)));
        }
        int rotation = 0; float? orientationConfidence = null;
        if (page.AutoOrient)
        {
            var value = DocumentProcessing.Classification(Execute("pp-lcnet-x1-0-doc-ori", "x", [1, 3, 224, 224], DocumentProcessing.Tensor(source, 224, 224, imagenet: true, classification: true))[0], 4);
            orientationConfidence = value.Confidence;
            if (value.Confidence >= .8f) rotation = value.Index * 90; else warnings.Add("orientation-low-confidence");
        }
        using var upright = source.Clone(c => c.Rotate(-rotation));
        PointD[] Original(PointD[] polygon) => polygon.Select(p => DocumentProcessing.ToSource(p, source.Width, source.Height, rotation)).ToArray();
        Region[] layout;
        if (page.Kind == "auto") layout = DocumentProcessing.DecodeRegions(Execute("pp-doclayout-l", "image", [1, 3, 640, 640], DocumentProcessing.Tensor(upright, 640, 640)), DocumentProcessing.Labels, upright.Width, upright.Height, .35f);
        else layout = [new(page.Kind, 1, new Box(0, 0, upright.Width, upright.Height))];
        if (layout.Length == 0) warnings.Add("no-layout-regions");
        DocumentLine[] lines = [];
        if (layout.Any(r => r.Category != "formula"))
        {
            if (ocr is null || ocrSize != (upright.Width, upright.Height))
            {
                ocr?.Dispose(); ocr = null;
                var profile = new Ocr.OcrModels("v5", "mobile", Path.Combine(models, "ppocrv5-mobile-det.onnx"), Path.Combine(models, "ppocrv5-mobile-rec.onnx"), Path.Combine(models, "ppocrv5_dict.txt"));
                Console.WriteLine($"Initializing HIP OCR: {upright.Width}x{upright.Height}");
                ocr = new Ocr.HipOcrPipeline(upright, ocrOptions, profile); ocrSize = (upright.Width, upright.Height);
            }
            else ocr.SetImage(upright);
            var text = Time("ocrPipeline", () => ocr.Run(dump is null ? null : Path.Combine(dump, "ocr"), dump is not null));
            lines = text.Value.Lines.Select(l => new DocumentLine(l.Text, l.Confidence, l.Polygon, Original(l.Polygon))).ToArray();
            foreach (var line in lines) if (string.IsNullOrWhiteSpace(line.Text) || line.Confidence < .8f) warnings.Add("ocr-low-confidence");
        }
        var regions = new List<DocumentRegion>();
        foreach (var region in layout)
        {
            var rect = region.Bounds.Rectangle(upright.Width, upright.Height);
            using var crop = upright.Clone(c => c.Crop(rect));
            var text = lines.Where(l => region.Bounds.Contains(l.Polygon.Average(p => p.X), l.Polygon.Average(p => p.Y))).ToArray();
            DocumentTable? table = null; DocumentFormula? formula = null;
            if (region.Category == "table")
            {
                var classification = DocumentProcessing.Classification(Execute("pp-lcnet-x1-0-table-cls", "x", [1, 3, 224, 224], DocumentProcessing.Tensor(crop, 224, 224, imagenet: true, classification: true))[0], 2);
                var style = classification.Index == 0 ? "wired" : "wireless";
                var cells = DocumentProcessing.DecodeRegions(Execute("rt-detr-l-" + style + "-cell-det", "image", [1, 3, 640, 640], DocumentProcessing.Tensor(crop, 640, 640)), ["cell"], crop.Width, crop.Height, .5f);
                if (!tables.TryGetValue(style, out var structureSession))
                {
                    Console.WriteLine("Initializing GPU table encoder/decoder: " + style);
                    tables.Add(style, structureSession = new TableStructureSession(native, models, style));
                }
                var structureInput = DocumentProcessing.Tensor(crop, 512, 512, imagenet: true, bgr: true, pad: true);
                var structure = Time("slanext-" + style, () => structureSession.Run(structureInput, dump is null ? null : Path.Combine(dump, "table-" + regions.Count)));
                var decoded = DocumentProcessing.DecodeTable(structure);
                var diagnostics = new List<string>(); string? html = null;
                if (!decoded.Complete) diagnostics.Add("structure-missing-eos");
                if (classification.Confidence < .8f) diagnostics.Add("table-style-low-confidence");
                if (diagnostics.Count == 0)
                {
                    var aligned = AlignCells(decoded.Markup, cells, diagnostics);
                    if (aligned is not null)
                    {
                        var cellText = aligned.Select(cell => string.Join(" ", text.Where(l => cell.Bounds.Contains(l.Polygon.Average(p => p.X) - rect.X, l.Polygon.Average(p => p.Y) - rect.Y)).Select(l => l.Text))).ToArray();
                        html = DocumentProcessing.FillTable(decoded.Markup, cellText);
                    }
                }
                table = new(style, decoded.Markup, html, cells.Select(c => c with { Bounds = c.Bounds with { X = c.Bounds.X + rect.X, Y = c.Bounds.Y + rect.Y } }).ToArray(), diagnostics.ToArray());
                warnings.AddRange(diagnostics.Select(d => "table:" + d));
            }
            else if (region.Category == "formula")
            {
                if (formulaBackend == "disabled") warnings.Add("formula-recognition-disabled");
                else
                {
                    formulaTokenizer ??= new FormulaTokenizer(Path.Combine(models, "pp-formulanet-plus-s.inference.yml"));
                    formulaSession ??= new FormulaSession(Path.Combine(models, "pp-formulanet-plus-s.onnx"));
                    var input = FormulaTokenizer.Prepare(crop);
                    var prediction = Time("formula-ort-cpu", () => formulaSession.Run(input));
                    var value = formulaTokenizer.Decode(prediction); formula = new("ort-cpu", value.Latex, value.Complete);
                    if (!value.Complete) warnings.Add("formula-missing-eos");
                    if (string.IsNullOrWhiteSpace(value.Latex)) warnings.Add("formula-empty");
                }
            }
            regions.Add(new(region, Original(region.Bounds.Polygon), text, table, formula));
        }
        return new(page.Id, upright.Width, upright.Height, rotation, orientationConfidence, regions.ToArray(), lines, warnings.Distinct().ToArray(), stages);
    }

    public static Region[]? AlignCells(string markup, Region[] cells, List<string> diagnostics)
    {
        XElement root;
        try
        {
            using var reader = XmlReader.Create(new StringReader(markup), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
            root = XElement.Load(reader);
            if (root.Name != "table" || root.Descendants().Any(e => e.Name.LocalName is not ("thead" or "tbody" or "tr" or "td") || e.HasAttributes && e.Name != "td")) throw new XmlException();
            if (root.Descendants("td").Any(c => c.HasElements || c.Value.Length != 0 || c.Attributes().Any(a => a.Name.LocalName is not ("colspan" or "rowspan") || !int.TryParse(a.Value, out var span) || span is < 1 or > 20))) throw new XmlException();
        }
        catch (XmlException) { diagnostics.Add("invalid-structure-markup"); return null; }
        if (root.Descendants("td").Any(c => (int?)c.Attribute("rowspan") > 1)) { diagnostics.Add("rowspan-needs-review"); return null; }
        var rows = root.Descendants("tr").Select(r => r.Elements("td").Count()).ToArray();
        if (rows.Length == 0 || rows.Any(c => c == 0) || rows.Sum() != cells.Length) { diagnostics.Add("cell-count-mismatch"); return null; }
        var sorted = cells.OrderBy(c => c.Bounds.Y).ThenBy(c => c.Bounds.X).ToArray(); var ordered = new List<Region>(); int offset = 0; double previousY = double.NegativeInfinity;
        foreach (var count in rows)
        {
            var row = sorted.Skip(offset).Take(count).OrderBy(c => c.Bounds.X).ToArray(); var top = row.Min(c => c.Bounds.Y);
            if (top <= previousY + 1 || row.Max(c => c.Bounds.Y) - top > Math.Max(2, row.Min(c => c.Bounds.Height) * .4) || row.Zip(row.Skip(1)).Any(p => p.First.Bounds.X + p.First.Bounds.Width - p.Second.Bounds.X > Math.Min(p.First.Bounds.Width, p.Second.Bounds.Width) * .2))
            { diagnostics.Add("ambiguous-cell-geometry"); return null; }
            ordered.AddRange(row); previousY = top; offset += count;
        }
        return ordered.ToArray();
    }
    public void Dispose() { ocr?.Dispose(); formulaSession?.Dispose(); foreach (var model in sessions.Values) model.Dispose(); foreach (var model in tables.Values) model.Dispose(); }
}
