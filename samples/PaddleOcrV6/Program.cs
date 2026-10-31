using MIGraphXSharp.Samples.PaddleOcrV6;
using MIGraphXSharp.Samples.Vision;

try
{
    var options = new Arguments(args);
    var output = Path.GetFullPath(options.Required("output"));
    Directory.CreateDirectory(output);
    using var image = Images.Load(options.Required("image"));
    using var pipeline = new OcrPipeline(image, options);
    var measured = Benchmarking.Measure(() => pipeline.Run(),
        options.Integer("warmup", 0, minimum: 0, maximum: 1000), options.Integer("iterations", 1, maximum: 10000));
    // Save one final demonstration outside the benchmark so tensor/PNG I/O never enters latency samples.
    var result = pipeline.Run(output, options.Has("dump-tensors")).Value;
    if (!result.AcceptedText.SequenceEqual(measured.Value.AcceptedText)) throw new InvalidOperationException("OCR text changed between measurement and artifact capture.");
    using var visualization = new Visualization(image);
    foreach (var line in result.Lines) visualization.Polygon(line.Polygon, $"{line.Text} {line.Confidence:F2}", "#2050ff");
    visualization.Save(output);
    File.WriteAllLines(Path.Combine(output, "text.txt"), result.AcceptedText);
    JsonOutput.Write(Path.Combine(output, "result.json"), new
    {
        task = "paddleocr-v6", image = new { image.Width, image.Height }, detector = pipeline.DetectorInfo,
        recognizer = pipeline.RecognizerInfo, detectionWithHostCopiesMilliseconds = result.DetectionMilliseconds,
        recognitionWithHostCopiesMilliseconds = result.RecognitionMilliseconds, lines = result.Lines, acceptedText = result.AcceptedText,
        timingScope = "pipeline=detector preprocessing+inference+DB decode+perspective crops+recognizer preprocessing+inference+CTC decode; excludes image I/O, compilation, visualization and artifact I/O. inference stages=Program.Run+host readback; excludes argument/map setup. recognitionLine pools all measured text lines.",
        performance = measured.Report,
    });
    Console.WriteLine(string.Join(Environment.NewLine, result.AcceptedText));
    Console.WriteLine($"Pipeline mean {measured.Report.Pipeline.Mean:F2} ms, P50 {measured.Report.Pipeline.P50:F2} ms, P95 {measured.Report.Pipeline.P95:F2} ms; {measured.Report.ImagesPerSecond:F2} images/s.");
    if (result.AcceptedText.Length < options.Integer("expect-min-lines", 0, minimum: 0, maximum: 3000)) throw new InvalidOperationException("Fewer recognized lines than required.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.Error.WriteLine("Usage: --native /path/libmigraphx_c.so --rec-model rec.onnx --dictionary dict.txt --image text.jpg --output /outside/source/results [--det-model det.onnx] [--rec-width 320] [--dump-tensors true] [--warmup 5 --iterations 30]");
    return 1;
}
