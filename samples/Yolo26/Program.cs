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
    var prepared = Images.PrepareYolo(image, size, size, task == "classify");
    var labels = options.Has("labels") ? File.ReadAllLines(options.Required("labels")) : Array.Empty<string>();
    using var session = new InferenceSession(options.Required("native"), model, "images", size, size);
    var (tensors, milliseconds) = session.Run(prepared.Tensor, options.Has("dump-tensors") ? Path.Combine(output, "tensors") : null);
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
        var detections = Decoder.Decode(task, tensors, prepared, image.Width, image.Height, labels, options.Probability("threshold", .25f),
            options.Integer("max-detections", 100, maximum: 300), visualization, output);
        results = detections; count = detections.Length;
    }
    visualization.Save(output);
    JsonOutput.Write(Path.Combine(output, "result.json"), new
    {
        task, model = Path.GetFileName(model), session.ModelSha256, session.AssemblyIdentity,
        image = new { image.Width, image.Height }, inputSize = size, outputShapes = tensors.Select(tensor => tensor.Shape),
        session.ParseMilliseconds, session.CompileMilliseconds, runWithHostCopiesMilliseconds = milliseconds, count, results,
    });
    Console.WriteLine($"{task}: {count} results; inference and host copies {milliseconds:F2} ms; output={output}");
    if (count < options.Integer("expect-min-results", 0, minimum: 0, maximum: 300))
        throw new InvalidOperationException("Fewer results than --expect-min-results.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.Error.WriteLine("Usage: --native /path/libmigraphx_c.so --task detect|classify|segment|pose|obb --model model.onnx --image photo.jpg --output /outside/source/results [--labels labels.txt] [--dump-tensors true]");
    return 1;
}
