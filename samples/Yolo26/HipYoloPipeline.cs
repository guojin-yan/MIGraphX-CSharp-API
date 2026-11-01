using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Streams;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static MIGraphXSharp.Samples.Yolo26.HipRuntimeContext;

namespace MIGraphXSharp.Samples.Yolo26;

public sealed record HipYoloResult(Detection[] Detections, Classification[] Classes);

/// <summary>Upload RGB, GPU resize/layout, inference, GPU decode/masks, compact result readback.</summary>
public sealed class HipYoloPipeline : IDisposable
{
    private readonly HipRuntimeContext gpu;
    private readonly HipInferenceSession session;
    private readonly HipDeviceMemory image, horizontal, resized, selected, records, count, error, maskPixels;
    private readonly HipDeviceMemory? logits, masks;
    private readonly byte[] pixels;
    private readonly HipEvent[] events;
    private readonly int sw, sh, size, rw, rh, left, top, maximum, fields, taskCode, candidates;
    private readonly string task;
    private readonly string[] labels;
    private readonly float threshold;
    public object ModelInfo => new { session.ModelSha256, session.ParseMilliseconds, session.CompileMilliseconds,
        outputShapes = session.OutputShapes.Select(s => s.Lengths), backend = "hip", deviceOutput = "D2D into reusable buffers" };

    public HipYoloPipeline(Image<Rgb24> source, Arguments options)
    {
        task = options.Required("task");
        taskCode = task switch { "detect" => 0, "segment" => 1, "pose" => 2, "obb" => 3, "classify" => 4, _ => throw new ArgumentException("Unknown YOLO task.") };
        sw = source.Width; sh = source.Height;
        size = options.Integer("size", task == "classify" ? 224 : task == "obb" ? 1024 : 640);
        maximum = options.Integer("max-detections", 100, maximum: 300);
        threshold = options.Probability("threshold", .25f);
        labels = options.Has("labels") ? File.ReadAllLines(options.Required("labels")) : [];
        var scale = Math.Min((double)size / sw, (double)size / sh);
        rw = task == "classify" ? size : (int)Math.Round(sw * scale);
        rh = task == "classify" ? size : (int)Math.Round(sh * scale);
        if (rw < 1 || rh < 1) throw new ArgumentException("The image aspect ratio produces an empty resized image.");
        left = (int)Math.Round((size - rw) / 2d - .1); top = (int)Math.Round((size - rh) / 2d - .1);
        gpu = new HipRuntimeContext(options);
        try
        {
            session = new HipInferenceSession(gpu, options.Required("native"), options.Required("model"), "images", size, size);
            fields = task switch { "detect" => 6, "segment" => 38, "pose" => 57, "obb" => 7, _ => 0 };
            var shape = session.OutputShapes[0].Lengths;
            if (task == "classify")
            {
                if (shape.Count != 2 || shape[0] != 1 || session.Outputs.Length != 1) throw new InvalidOperationException("Expected [1,classes].");
                candidates = checked((int)shape[1]);
            }
            else
            {
                if (shape.Count != 3 || shape[0] != 1 || shape[2] != fields) throw new InvalidOperationException("Expected an end-to-end YOLO26 export.");
                candidates = checked((int)shape[1]);
                if (task == "segment" && (session.Outputs.Length != 2 || !session.OutputShapes[1].Lengths.SequenceEqual(new long[] { 1, 32, 160, 160 })))
                    throw new InvalidOperationException("Expected [1,32,160,160] mask prototypes.");
                if (task != "segment" && session.Outputs.Length != 1) throw new InvalidOperationException("Unexpected output count.");
            }
            (image, pixels) = gpu.Image(source);
            horizontal = gpu.Allocate(checked((long)rw * (task == "classify" ? Math.Min(sw, sh) : sh) * 3 * sizeof(float)));
            resized = gpu.Allocate(checked((long)rw * rh * 3));
            selected = gpu.Allocate(maximum * sizeof(int)); records = gpu.Allocate(Math.Max(10, maximum * 61) * sizeof(float));
            count = gpu.Allocate(sizeof(int)); error = gpu.Allocate(sizeof(int)); maskPixels = gpu.Allocate(maximum * sizeof(int));
            if (task == "segment")
            {
                logits = gpu.Allocate(checked((long)maximum * 160 * 160 * sizeof(float)));
                masks = gpu.Allocate(checked((long)maximum * sw * sh));
            }
            events = Enumerable.Range(0, 5).Select(_ => gpu.Runtime.CreateEvent()).ToArray();
        }
        catch { session?.Dispose(); gpu.Dispose(); throw; }
    }

    public TimedValue<HipYoloResult> Run()
    {
        events[0].Record(gpu.Stream);
        image.CopyFromAsync(pixels, gpu.Stream);
        events[1].Record(gpu.Stream);
        int crop = task == "classify" ? Math.Min(sw, sh) : 0;
        gpu.Resize(image, sw, sh, horizontal, resized, rw, rh, crop == 0 ? 0 : (sw - crop) / 2,
            crop == 0 ? 0 : (sh - crop) / 2, crop, cubic: task == "classify");
        gpu.Launch("tensorize", checked(size * size * 3), P(resized), P(session.Input), I(rw), I(rh), I(size), I(size), I(left), I(top), I(0));
        events[2].Record(gpu.Stream);
        using var run = session.Enqueue();
        events[3].Record(gpu.Stream);
        gpu.Launch("clear_int", 1, P(error), I(1), I(0));
        foreach (var (buffer, index) in session.Outputs.Select((b, i) => (b, i)))
            gpu.Launch("check_finite", checked((int)session.OutputShapes[index].ElementCount), P(buffer), I(checked((int)session.OutputShapes[index].ElementCount)), P(error));
        Detection[] detections = []; Classification[] classes = [];
        if (task == "classify")
        {
            gpu.Launch("classify_top", candidates, P(session.Outputs[0]), I(candidates), P(records), P(error));
            events[4].Record(gpu.Stream);
            var top = gpu.Read<float>(records, Math.Min(5, candidates) * 2);
            classes = Enumerable.Range(0, Math.Min(5, candidates)).Select(i => new Classification((int)top[i * 2], Label((int)top[i * 2]), top[i * 2 + 1])).ToArray();
        }
        else
        {
            gpu.Launch("clear_int", 1, P(count), I(1), I(0));
            gpu.Launch("yolo_select", candidates, P(session.Outputs[0]), I(candidates), I(fields), I(taskCode), I(maximum), F(threshold),
                I(size), I(size), I(rw), I(rh), I(left), I(top), I(sw), I(sh), P(selected), P(records), P(count), P(error));
            if (task == "segment")
            {
                gpu.Launch("mask_logits", checked(maximum * 160 * 160), P(session.Outputs[0]), P(session.Outputs[1]), P(selected), P(count), P(logits!), I(fields));
                gpu.Launch("mask_restore", checked(maximum * sw * sh), P(session.Outputs[0]), P(logits!), P(selected), P(count), I(fields),
                    I(size), I(size), I(sw), I(sh), I(left), I(top), P(masks!));
                gpu.Launch("mask_count", checked(maximum * 256), P(masks!), P(count), I(checked(sw * sh)), P(maskPixels));
            }
            events[4].Record(gpu.Stream);
            var n = gpu.Read<int>(count, 1)[0];
            if (n < 0 || n > maximum) throw new InvalidOperationException("Invalid GPU detection count.");
            var values = gpu.Read<float>(records, n * 61);
            var areas = task == "segment" && n > 0 ? gpu.Read<int>(maskPixels, n) : new int[n];
            detections = Enumerable.Range(0, n).Select(i =>
            {
                int o = i * 61, id = checked((int)values[o]);
                var polygon = Enumerable.Range(0, 4).Select(k => new PointD(values[o + 2 + k * 2], values[o + 3 + k * 2])).ToArray();
                var points = task == "pose" ? Enumerable.Range(0, 17).Select(k => new Keypoint(values[o + 10 + k * 3], values[o + 11 + k * 3], values[o + 12 + k * 3])).ToArray() : null;
                return new Detection(id, Label(id), values[o + 1], polygon, points, task == "segment" ? $"mask-{i:D3}.png" : null, areas[i]);
            }).ToArray();
        }
        if (gpu.Read<int>(error, 1)[0] != 0) throw new InvalidOperationException("GPU decoding found non-finite values or invalid probabilities.");
        run.Synchronize();
        string[] names = ["uploadGpu", "preprocessGpu", "inferenceGpuWithDeviceCopies", "postprocessGpu"];
        return new TimedValue<HipYoloResult>(new(detections, classes), Enumerable.Range(0, 4).ToDictionary(i => names[i], i => new[] { (double)HipEvent.ElapsedTime(events[i], events[i + 1]) }));
    }

    private string Label(int id)
    {
        if (id < 0 || (labels.Length > 0 && id >= labels.Length)) throw new InvalidOperationException("Labels do not match the model.");
        return id < labels.Length ? labels[id] : $"class-{id}";
    }

    public void Save(Image<Rgb24> source, HipYoloResult result, string directory, bool dump)
    {
        using var visualization = new Visualization(source);
        if (task == "classify") foreach (var (c, i) in result.Classes.Select((c, i) => (c, i))) visualization.Label(new PointD(10, 30 + i * 24), $"{c.Label} {c.Score:F3}");
        var maskData = task == "segment" && result.Detections.Length > 0 ? gpu.Read<byte>(masks!, checked(result.Detections.Length * sw * sh)) : null;
        for (var i = 0; i < result.Detections.Length; i++)
        {
            var d = result.Detections[i]; visualization.Polygon(d.Polygon, $"{d.Label} {d.Score:F2}");
            if (d.Keypoints is not null) Decoder.DrawPose(d.Keypoints, visualization);
            if (maskData is not null)
            {
                using var mask = Image.LoadPixelData<L8>(maskData.AsSpan(i * sw * sh, sw * sh), sw, sh);
                visualization.Mask(mask, d.Mask!, directory);
            }
        }
        visualization.Save(directory);
        if (dump) session.Dump(Path.Combine(directory, "tensors"), size, size);
    }

    public void Dispose()
    {
        gpu.Stream.Synchronize();
        foreach (var item in events) item.Dispose();
        session.Dispose(); gpu.Dispose();
    }
}
