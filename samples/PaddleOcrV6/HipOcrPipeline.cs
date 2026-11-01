using System.Text;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Streams;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static MIGraphXSharp.Samples.PaddleOcrV6.HipRuntimeContext;

namespace MIGraphXSharp.Samples.PaddleOcrV6;

/// <summary>GPU DB decode, perspective crops, recognition preparation, argmax and CTC collapse.</summary>
public sealed class HipOcrPipeline : IDisposable
{
    private const int MaximumLines = 3000;
    private readonly HipRuntimeContext gpu;
    private readonly HipInferenceSession recognizer;
    private readonly HipInferenceSession? detector;
    private readonly HipDeviceMemory image, crop, cropHorizontal, meta, boxes, sorted, order, control, resultCount;
    private readonly HipDeviceMemory ids, scores, tokens, confidence;
    private readonly HipDeviceMemory? horizontal, resized, parents, counts, components, offsets, sizes, points, hull;
    private readonly byte[] pixels;
    private readonly string[] dictionary;
    private readonly int sw, sh, dw, dh, recWidth, steps, maxEdge;
    private readonly float detThreshold, boxThreshold, textThreshold;
    private readonly HipEvent[] detEvents;
    private readonly List<HipEvent[]> recEvents = [];
    public object RecognizerInfo => new { recognizer.ModelSha256, recognizer.ParseMilliseconds, recognizer.CompileMilliseconds,
        inputWidth = recWidth, classes = dictionary.Length, backend = "hip" };
    public object? DetectorInfo => detector is null ? null : new { detector.ModelSha256, detector.ParseMilliseconds, detector.CompileMilliseconds,
        inputWidth = dw, inputHeight = dh, dbCandidateLimit = MaximumLines };

    public HipOcrPipeline(Image<Rgb24> source, Arguments options)
    {
        sw = source.Width; sh = source.Height;
        recWidth = options.Integer("rec-width", 320);
        maxEdge = Math.Clamp((int)Math.Ceiling(Math.Sqrt((double)sw * sw + (double)sh * sh)) + 1, 1, 8192);
        dictionary = Recognition.LoadDictionary(options.Required("dictionary"));
        detThreshold = options.Probability("det-threshold", .2f); boxThreshold = options.Probability("box-threshold", .4f); textThreshold = options.Probability("text-threshold", .5f);
        gpu = new HipRuntimeContext(options);
        try
        {
            var native = options.Required("native");
            recognizer = new HipInferenceSession(gpu, native, options.Required("rec-model"), "x", recWidth, 48);
            var shape = recognizer.OutputShapes.Single().Lengths;
            if (shape.Count != 3 || shape[0] != 1 || shape[2] != dictionary.Length) throw new InvalidOperationException("Recognizer shape and dictionary do not agree.");
            steps = checked((int)shape[1]);
            if (options.Has("det-model"))
            {
                int limit = options.Integer("det-limit", 960);
                double ratio = Math.Min(1d, (double)limit / Math.Max(sw, sh));
                dw = Math.Max(32, (int)Math.Round(sw * ratio / 32) * 32); dh = Math.Max(32, (int)Math.Round(sh * ratio / 32) * 32);
                detector = new HipInferenceSession(gpu, native, options.Required("det-model"), "x", dw, dh);
                var detShape = detector.OutputShapes.Single().Lengths;
                if (!detShape.SequenceEqual(new long[] { 1, 1, dh, dw })) throw new InvalidOperationException("Expected a full-resolution [1,1,H,W] DB map.");
                horizontal = gpu.Allocate(checked((long)dw * sh * 3 * sizeof(float))); resized = gpu.Allocate(checked((long)dw * dh * 3));
                int n = checked(dw * dh);
                parents = gpu.Allocate((long)n * sizeof(int)); counts = gpu.Allocate((long)n * sizeof(int));
                components = gpu.Allocate((long)n * sizeof(int)); points = gpu.Allocate((long)n * sizeof(int)); hull = gpu.Allocate((long)n * sizeof(int));
                offsets = gpu.Allocate(MaximumLines * sizeof(int)); sizes = gpu.Allocate(MaximumLines * sizeof(int));
            }
            (image, pixels) = gpu.Image(source);
            crop = gpu.Allocate(checked((long)maxEdge * maxEdge * 3));
            cropHorizontal = gpu.Allocate(checked((long)maxEdge * recWidth * 3 * sizeof(float)));
            meta = gpu.Allocate(MaximumLines * 6 * sizeof(int)); boxes = gpu.Allocate(MaximumLines * 9 * sizeof(double)); sorted = gpu.Allocate(MaximumLines * 9 * sizeof(double));
            order = gpu.Allocate(MaximumLines * sizeof(int)); control = gpu.Allocate(2 * sizeof(int)); resultCount = gpu.Allocate(sizeof(int));
            ids = gpu.Allocate(steps * sizeof(int)); scores = gpu.Allocate(steps * sizeof(float));
            tokens = gpu.Allocate(checked((long)MaximumLines * (steps + 1) * sizeof(int))); confidence = gpu.Allocate(MaximumLines * sizeof(float));
            detEvents = Enumerable.Range(0, 5).Select(_ => gpu.Runtime.CreateEvent()).ToArray();
        }
        catch { detector?.Dispose(); recognizer?.Dispose(); gpu.Dispose(); throw; }
    }

    public TimedValue<OcrResult> Run(string? artifacts = null, bool dumpTensors = false)
    {
        detEvents[0].Record(gpu.Stream);
        image.CopyFromAsync(pixels, gpu.Stream);
        gpu.Launch("clear_int", 2, P(control), I(2), I(0));
        detEvents[1].Record(gpu.Stream);
        if (detector is not null)
        {
            gpu.Resize(image, sw, sh, horizontal!, resized!, dw, dh);
            gpu.Launch("tensorize", checked(dw * dh * 3), P(resized!), P(detector.Input), I(dw), I(dh), I(dw), I(dh), I(0), I(0), I(1));
            detEvents[2].Record(gpu.Stream);
            using var run = detector.Enqueue();
            detEvents[3].Record(gpu.Stream);
            int n = checked(dw * dh);
            // control[1] is the error word. A separate buffer view is not needed:
            // validation kernels receive a dedicated word below via resultCount during decode.
            gpu.Launch("clear_int", 1, P(resultCount), I(1), I(0));
            gpu.Launch("db_init", n, P(detector.Outputs[0]), P(parents!), P(counts!), I(n), F(detThreshold), P(resultCount));
            gpu.Launch("db_union", n, P(parents!), I(dw), I(dh));
            gpu.Launch("db_count", n, P(detector.Outputs[0]), P(parents!), P(counts!), I(dw), I(dh), F(detThreshold));
            gpu.Serial("db_offsets", P(parents!), P(counts!), P(components!), P(offsets!), P(sizes!), I(n), P(control));
            gpu.Launch("db_gather", n, P(detector.Outputs[0]), P(parents!), P(counts!), P(components!), P(offsets!), P(points!), I(dw), I(dh), F(detThreshold));
            gpu.Launch("db_rectangles", MaximumLines, P(detector.Outputs[0]), P(points!), P(hull!), P(offsets!), P(sizes!), P(control), P(boxes), I(dw), I(dh), I(sw), I(sh), F(boxThreshold));
            // Check the map error before reusing resultCount for the final compact count.
            if (gpu.Read<int>(resultCount, 1)[0] != 0) throw new InvalidOperationException("DB output is not a finite probability map.");
            gpu.Serial("db_sort", P(boxes), P(sorted), P(control), P(order), P(resultCount));
            detEvents[4].Record(gpu.Stream);
            run.Synchronize();
        }
        else
        {
            gpu.Write(sorted, new double[] { 0, 0, sw - 1, 0, sw - 1, sh - 1, 0, sh - 1, 1 });
            gpu.Write(resultCount, new[] { 1 });
            for (var i = 2; i < 5; i++) detEvents[i].Record(gpu.Stream);
        }
        var lineCount = gpu.Read<int>(resultCount, 1)[0];
        if (lineCount < 0 || lineCount > MaximumLines || gpu.Read<int>(control, 2)[1] != 0) throw new InvalidOperationException("DB component capacity exceeded; increase the documented kernel limit before using this image.");
        // Crop geometry is computed on GPU; only small integer launch dimensions reach the CPU.
        gpu.Launch("clear_int", 1, P(control), I(1), I(0));
        for (var line = 0; line < lineCount; line++) gpu.Serial("crop_meta", P(sorted), I(line), P(meta), I(recWidth), I(maxEdge), P(control));
        var dimensions = gpu.Read<int>(meta, lineCount * 6);
        if (gpu.Read<int>(control, 1)[0] != 0) throw new InvalidOperationException("Crop dimensions exceed the allocated scratch space.");
        for (var line = 0; line < lineCount; line++)
        {
            while (recEvents.Count <= line) recEvents.Add(Enumerable.Range(0, 4).Select(_ => gpu.Runtime.CreateEvent()).ToArray());
            var ev = recEvents[line]; ev[0].Record(gpu.Stream);
            int cropWidth = dimensions[line * 6 + 2], cropHeight = dimensions[line * 6 + 3], resizedWidth = dimensions[line * 6 + 5];
            gpu.Launch("perspective_crop", checked(cropWidth * cropHeight * 3), P(image), P(crop), I(sw), I(sh), P(sorted), I(line), P(meta));
            gpu.Launch("rec_resize_x", checked(resizedWidth * cropHeight * 3), P(crop), P(cropHorizontal), P(meta), I(line));
            gpu.Launch("rec_resize_tensor", checked(recWidth * 48 * 3), P(cropHorizontal), P(recognizer.Input), P(meta), I(recWidth), I(line));
            ev[1].Record(gpu.Stream);
            using var run = recognizer.Enqueue(); ev[2].Record(gpu.Stream);
            gpu.Launch("ctc_argmax", checked(steps * 256), P(recognizer.Outputs[0]), I(dictionary.Length), I(steps), P(ids), P(scores), P(control));
            gpu.Serial("ctc_compact", P(ids), P(scores), I(steps), I(line), P(tokens), P(confidence));
            ev[3].Record(gpu.Stream);
            // Reuse fixed-batch model buffers only after the preceding inference and its
            // postprocessing finish. Future batching can be added for compatible exports.
            run.Synchronize();
            if (artifacts is not null)
            {
                Directory.CreateDirectory(artifacts);
                using var cropImage = Image.LoadPixelData<Rgb24>(gpu.Read<byte>(crop, checked(cropWidth * cropHeight * 3)), cropWidth, cropHeight);
                cropImage.SaveAsPng(Path.Combine(artifacts, $"crop-{line:D3}.png"));
                if (dumpTensors) recognizer.Dump(Path.Combine(artifacts, $"rec-tensors-{line:D3}"), recWidth, 48);
            }
        }
        var packedTokens = gpu.Read<int>(tokens, checked(lineCount * (steps + 1)));
        var confidences = gpu.Read<float>(confidence, lineCount);
        var polygons = gpu.Read<double>(sorted, lineCount * 9);
        if (gpu.Read<int>(control, 1)[0] != 0) throw new InvalidOperationException("GPU CTC decoding found invalid output values.");
        var lines = new TextLine[lineCount]; var texts = new List<string>();
        for (var line = 0; line < lineCount; line++)
        {
            int offset = line * (steps + 1), n = packedTokens[offset];
            if (n < 0 || n > steps) throw new InvalidOperationException("Invalid GPU CTC token count.");
            var text = new StringBuilder();
            for (var j = 0; j < n; j++)
            {
                var id = packedTokens[offset + 1 + j];
                if (id <= 0 || id >= dictionary.Length) throw new InvalidOperationException("GPU CTC class does not match the dictionary.");
                text.Append(dictionary[id]); // Final Unicode presentation; CTC decoding already ran on GPU.
            }
            var quad = Enumerable.Range(0, 4).Select(k => new PointD(polygons[line * 9 + k * 2], polygons[line * 9 + k * 2 + 1])).ToArray();
            lines[line] = new TextLine(line, quad, (float)polygons[line * 9 + 8], text.ToString(), confidences[line], $"crop-{line:D3}.png");
            if (confidences[line] >= textThreshold) texts.Add(text.ToString());
        }
        var recognitionSamples = Enumerable.Range(0, lineCount).Select(i => (double)HipEvent.ElapsedTime(recEvents[i][1], recEvents[i][2])).ToArray();
        double detTime = detector is null ? 0 : HipEvent.ElapsedTime(detEvents[2], detEvents[3]), recTime = recognitionSamples.Sum();
        var stages = new Dictionary<string, double[]>
        {
            ["uploadGpu"] = [HipEvent.ElapsedTime(detEvents[0], detEvents[1])],
            ["detectorPreprocessGpu"] = [HipEvent.ElapsedTime(detEvents[1], detEvents[2])],
            ["detectorInferenceGpuWithDeviceCopies"] = [detTime],
            ["detectorPostprocessGpu"] = [HipEvent.ElapsedTime(detEvents[3], detEvents[4])],
            ["inferenceGpuWithDeviceCopiesTotal"] = [detTime + recTime],
            ["recognitionPreprocessGpuTotal"] = [Enumerable.Range(0, lineCount).Sum(i => (double)HipEvent.ElapsedTime(recEvents[i][0], recEvents[i][1]))],
            ["recognitionPostprocessGpuTotal"] = [Enumerable.Range(0, lineCount).Sum(i => (double)HipEvent.ElapsedTime(recEvents[i][2], recEvents[i][3]))],
        };
        if (lineCount > 0) stages["recognitionLineGpuWithDeviceCopies"] = recognitionSamples;
        if (artifacts is not null && dumpTensors && detector is not null) detector.Dump(Path.Combine(artifacts, "det-tensors"), dw, dh);
        return new(new OcrResult(lines, texts.ToArray(), detTime, recTime), stages);
    }

    public void Dispose()
    {
        gpu.Stream.Synchronize();
        foreach (var e in detEvents.Concat(recEvents.SelectMany(e => e))) e.Dispose();
        detector?.Dispose(); recognizer.Dispose(); gpu.Dispose();
    }
}
