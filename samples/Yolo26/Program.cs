using MIGraphXSharp.Samples.Vision;
using MIGraphXSharp.Samples.Yolo26;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

try
{
    var options = new Arguments(args);
    var task = options.Required("task");
    if (task is not ("detect" or "classify" or "segment" or "pose" or "obb")) throw new ArgumentException("--task detect|classify|segment|pose|obb");
    var model = Path.GetFullPath(options.Required("model"));
    var output = Path.GetFullPath(options.Required("output"));
    Directory.CreateDirectory(output);
    using var image = Images.Load(options.Required("image"));
    var defaultSize = task == "classify" ? 224 : task == "obb" ? 1024 : 640;
    var size = options.Integer("size", defaultSize);
    var labels = options.Has("labels") ? File.ReadAllLines(options.Required("labels")) : Array.Empty<string>();
    var threshold = options.Probability("threshold", .25f);
    var maximum = options.Integer("max-detections", 100, maximum: 300);
    using var session = new InferenceSession(options.Required("native"), model, "images", size, size);
    var measured = Benchmarking.Measure(() =>
    {
        var input = Images.PrepareYolo(image, size, size, task == "classify");
        var (values, elapsed) = session.Run(input.Tensor);
        if (task == "classify") _ = Decoder.Classify(values[0], labels);
        else _ = Decoder.Decode(task, values, input, image.Width, image.Height, labels, threshold, maximum, null, null);
        return new TimedValue<(Tensor[], Letterbox, double)>((values, input, elapsed),
            new Dictionary<string, double[]> { ["inference"] = [elapsed] });
    }, options.Integer("warmup", 0, minimum: 0, maximum: 1000), options.Integer("iterations", 1, maximum: 10000));
    var (tensors, prepared, milliseconds) = measured.Value;
    if (options.Has("dump-tensors")) session.Dump(prepared.Tensor, tensors, Path.Combine(output, "tensors"));
    using var visualization = new Visualization(image);
    object results;
    int count;
    if (task == "classify")
    {
        var classes = Decoder.Classify(tensors[0], labels);
        results = classes; count = classes.Length;
        foreach (var (item, index) in classes.Select((item, index) => (item, index))) visualization.Label(new PointD(10, 30 + index * 24), $"{item.Label} {item.Score:F3}");
    }
    else
    {
        var detections = Decoder.Decode(task, tensors, prepared, image.Width, image.Height, labels, threshold, maximum, visualization, output);
        results = detections; count = detections.Length;
    }
    visualization.Save(output);
    JsonOutput.Write(Path.Combine(output, "result.json"), new
    {
        task, model = Path.GetFileName(model), session.ModelSha256, session.AssemblyIdentity,
        image = new { image.Width, image.Height }, inputSize = size, outputShapes = tensors.Select(tensor => tensor.Shape),
        session.ParseMilliseconds, session.CompileMilliseconds, runWithHostCopiesMilliseconds = milliseconds, count, results,
        timingScope = "pipeline=preprocessing+argument/map setup+inference+host readback+decode (including masks); excludes image I/O, compilation, visualization and artifact I/O. inference=Program.Run+host readback; excludes argument/map setup.",
        performance = measured.Report,
    });
    Console.WriteLine($"{task}: {count} results; inference and host copies {milliseconds:F2} ms; output={output}");
    Console.WriteLine($"Pipeline mean {measured.Report.Pipeline.Mean:F2} ms, P50 {measured.Report.Pipeline.P50:F2} ms, P95 {measured.Report.Pipeline.P95:F2} ms; {measured.Report.ImagesPerSecond:F2} images/s.");
    if (count < options.Integer("expect-min-results", 0, minimum: 0, maximum: 300))
        throw new InvalidOperationException("Fewer results than --expect-min-results.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.Error.WriteLine("Usage: --native /path/libmigraphx_c.so --task detect|classify|segment|pose|obb --model model.onnx --image photo.jpg --output /outside/source/results [--labels labels.txt] [--dump-tensors true] [--warmup 5 --iterations 30]");
    return 1;
}
