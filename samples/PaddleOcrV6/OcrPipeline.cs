using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MIGraphXSharp.Samples.PaddleOcrV6;

public sealed record TextLine(int Index, PointD[] Polygon, float DetectionConfidence, string Text, float Confidence, string Crop);
public sealed record OcrResult(TextLine[] Lines, string[] AcceptedText, double DetectionMilliseconds, double RecognitionMilliseconds);

/// <summary>Reusable compiled models; Run excludes visualization but includes preprocessing, crops and decode.</summary>
public sealed class OcrPipeline : IDisposable
{
    private readonly Image<Rgb24> image;
    private readonly InferenceSession recognizer;
    private readonly InferenceSession? detector;
    private readonly string[] dictionary;
    private readonly int recWidth;
    private readonly int detWidth;
    private readonly int detHeight;
    private readonly float detThreshold;
    private readonly float boxThreshold;
    private readonly float textThreshold;
    public object RecognizerInfo => new { recognizer.ModelSha256, recognizer.AssemblyIdentity, recognizer.ParseMilliseconds, recognizer.CompileMilliseconds, inputWidth = recWidth, classes = dictionary.Length };
    public object? DetectorInfo => detector is null ? null : new { detector.ModelSha256, detector.ParseMilliseconds, detector.CompileMilliseconds, inputWidth = detWidth, inputHeight = detHeight };

    public OcrPipeline(Image<Rgb24> image, Arguments options)
    {
        this.image = image;
        recWidth = options.Integer("rec-width", 320);
        dictionary = Recognition.LoadDictionary(options.Required("dictionary"));
        detThreshold = options.Probability("det-threshold", .2f);
        boxThreshold = options.Probability("box-threshold", .4f);
        textThreshold = options.Probability("text-threshold", .5f);
        var native = options.Required("native");
        recognizer = new InferenceSession(native, options.Required("rec-model"), "x", recWidth, 48);
        try
        {
            if (!options.Has("det-model")) return;
            var limit = options.Integer("det-limit", 960);
            var ratio = Math.Min(1d, (double)limit / Math.Max(image.Width, image.Height));
            detWidth = Math.Max(32, (int)Math.Round(image.Width * ratio / 32) * 32);
            detHeight = Math.Max(32, (int)Math.Round(image.Height * ratio / 32) * 32);
            detector = new InferenceSession(native, options.Required("det-model"), "x", detWidth, detHeight);
        }
        catch { recognizer.Dispose(); throw; }
    }

    public TimedValue<OcrResult> Run(string? artifacts = null, bool dumpTensors = false)
    {
        TextBox[] boxes;
        double detectionMilliseconds = 0;
        var recognitionSamples = new List<double>();
        if (detector is not null)
        {
            using var resized = image.Clone(context => context.Resize(detWidth, detHeight, KnownResamplers.Triangle));
            var input = Images.ToTensor(resized, rgb: false, mean: [.485f,.456f,.406f], std: [.229f,.224f,.225f]);
            var (tensors, milliseconds) = detector.Run(input, artifacts is not null && dumpTensors ? Path.Combine(artifacts, "det-tensors") : null);
            detectionMilliseconds = milliseconds;
            boxes = TextDetector.Decode(tensors.Single(), image.Width, image.Height, detThreshold, boxThreshold);
        }
        else boxes = [new TextBox([new(0,0),new(image.Width - 1,0),new(image.Width - 1,image.Height - 1),new(0,image.Height - 1)], 1)];
        var lines = new List<TextLine>();
        var texts = new List<string>();
        for (var index = 0; index < boxes.Length; ++index)
        {
            var box = boxes[index];
            using var crop = Images.Rectify(image, box.Polygon);
            var cropName = $"crop-{index:D3}.png";
            if (artifacts is not null) crop.SaveAsPng(Path.Combine(artifacts, cropName));
            var (tensors, milliseconds) = recognizer.Run(Recognition.Prepare(crop, recWidth),
                artifacts is not null && dumpTensors ? Path.Combine(artifacts, $"rec-tensors-{index:D3}") : null);
            var decoded = Recognition.Decode(tensors.Single(), dictionary);
            recognitionSamples.Add(milliseconds);
            lines.Add(new TextLine(index, box.Polygon, box.Score, decoded.Text, decoded.Confidence, cropName));
            if (decoded.Confidence >= textThreshold) texts.Add(decoded.Text);
        }
        var total = recognitionSamples.Sum();
        var stages = new Dictionary<string, double[]> { ["inferenceTotal"] = [detectionMilliseconds + total], ["recognitionTotal"] = [total] };
        if (recognitionSamples.Count > 0) stages["recognitionLine"] = recognitionSamples.ToArray();
        if (detector is not null) stages["detector"] = [detectionMilliseconds];
        return new TimedValue<OcrResult>(new OcrResult(lines.ToArray(), texts.ToArray(), detectionMilliseconds, total), stages);
    }

    public void Dispose() { detector?.Dispose(); recognizer.Dispose(); }
}
