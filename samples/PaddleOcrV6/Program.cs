using MIGraphXSharp.Samples.PaddleOcrV6;
using MIGraphXSharp.Samples.Vision;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

try
{
    var options = new Arguments(args);
    var native = options.Required("native"); var output = Path.GetFullPath(options.Required("output"));
    Directory.CreateDirectory(output);
    using var image = Images.Load(options.Required("image"));
    var dictionary = Recognition.LoadDictionary(options.Required("dictionary"));
    var recWidth = options.Integer("rec-width", 320);
    using var recognizer = new InferenceSession(native, options.Required("rec-model"), "x", recWidth, 48);
    TextBox[] boxes;
    object? detectorInfo = null;
    if (options.Has("det-model"))
    {
        var limit = options.Integer("det-limit", 960);
        var ratio = Math.Min(1d, (double)limit / Math.Max(image.Width, image.Height));
        var detWidth = Math.Max(32, (int)Math.Round(image.Width * ratio / 32) * 32);
        var detHeight = Math.Max(32, (int)Math.Round(image.Height * ratio / 32) * 32);
        using var resized = image.Clone(context => context.Resize(detWidth, detHeight, KnownResamplers.Triangle));
        var input = Images.ToTensor(resized, rgb: false, mean: [.485f,.456f,.406f], std: [.229f,.224f,.225f]);
        using var detector = new InferenceSession(native, options.Required("det-model"), "x", detWidth, detHeight);
        var (tensors, milliseconds) = detector.Run(input, options.Has("dump-tensors") ? Path.Combine(output, "det-tensors") : null);
        boxes = TextDetector.Decode(tensors.Single(), image.Width, image.Height, options.Probability("det-threshold", .2f), options.Probability("box-threshold", .4f));
        detectorInfo = new { detector.ModelSha256, detector.ParseMilliseconds, detector.CompileMilliseconds, runWithHostCopiesMilliseconds = milliseconds, inputWidth = detWidth, inputHeight = detHeight };
    }
    else boxes = [new TextBox([new(0,0),new(image.Width - 1,0),new(image.Width - 1,image.Height - 1),new(0,image.Height - 1)], 1)];
    var lines = new List<object>(); var texts = new List<string>(); double recognitionMilliseconds = 0;
    using var visualization = new Visualization(image);
    for (var index = 0; index < boxes.Length; ++index)
    {
        var box = boxes[index]; using var crop = Images.Rectify(image, box.Polygon);
        var cropName = $"crop-{index:D3}.png"; crop.SaveAsPng(Path.Combine(output, cropName));
        var (tensors, milliseconds) = recognizer.Run(Recognition.Prepare(crop, recWidth),
            options.Has("dump-tensors") ? Path.Combine(output, $"rec-tensors-{index:D3}") : null);
        var decoded = Recognition.Decode(tensors.Single(), dictionary);
        recognitionMilliseconds += milliseconds;
        lines.Add(new { index, box.Polygon, detectionConfidence = box.Score, decoded.Text, decoded.Confidence, crop = cropName });
        if (decoded.Confidence >= options.Probability("text-threshold", .5f)) texts.Add(decoded.Text);
        visualization.Polygon(box.Polygon, $"{decoded.Text} {decoded.Confidence:F2}", "#2050ff");
    }
    visualization.Save(output);
    File.WriteAllLines(Path.Combine(output, "text.txt"), texts);
    JsonOutput.Write(Path.Combine(output, "result.json"), new
    {
        task = "paddleocr-v6", image = new { image.Width, image.Height }, detector = detectorInfo,
        recognizer = new { recognizer.ModelSha256, recognizer.AssemblyIdentity, recognizer.ParseMilliseconds, recognizer.CompileMilliseconds, inputWidth = recWidth, classes = dictionary.Length },
        recognitionWithHostCopiesMilliseconds = recognitionMilliseconds, lines, acceptedText = texts,
    });
    Console.WriteLine(string.Join(Environment.NewLine, texts));
    if (texts.Count < options.Integer("expect-min-lines", 0, minimum: 0, maximum: 3000)) throw new InvalidOperationException("Fewer recognized lines than required.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.Error.WriteLine("Usage: --native /path/libmigraphx_c.so --rec-model rec.onnx --dictionary dict.txt --image text.jpg --output /outside/source/results [--det-model det.onnx] [--rec-width 320] [--dump-tensors true]");
    return 1;
}
