using System.Text;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static MIGraphXSharp.Samples.PPStructure.Ocr.HipRuntimeContext;

namespace MIGraphXSharp.Samples.PPStructure.Ocr;

/// <summary>GPU DB decode, perspective crops, recognition preparation, argmax and CTC collapse.</summary>
public sealed class HipOcrPipeline : IDisposable
{
    private const int MaximumLines = 3000;
    private readonly HipRuntimeContext gpu;
    private readonly List<RecognitionWorker> workers = [];
    private HipInferenceSession Recognizer => workers[0].Session;
    private readonly HipInferenceSession? detector;
    private readonly HipDeviceMemory image, meta, boxes, sorted, order, control, resultCount;
    private readonly HipDeviceMemory tokens, confidence;
    private readonly HipDeviceMemory? horizontal, resized, parents, counts, components, offsets, sizes, points, hull, blockRoots, blockPoints;
    private readonly byte[] pixels;
    private readonly string[] dictionary;
    private readonly int sw, sh, dw, dh, recWidth, steps, maxEdge, batchSize;
    private readonly float detThreshold, boxThreshold, textThreshold;
    private readonly HipEvent[] detEvents;
    private readonly List<HipEvent[]> recEvents = [];
    public object RecognizerInfo => new
    {
        Recognizer.ModelSha256,
        parseMilliseconds = workers.Sum(w => w.Session.ParseMilliseconds),
        compileMilliseconds = workers.Sum(w => w.Session.CompileMilliseconds),
        inputWidth = recWidth,
        classes = dictionary.Length,
        batchSize,
        instances = workers.Count,
        backend = "hip"
    };
    public object? DetectorInfo => detector is null ? null : new
    {
        detector.ModelSha256,
        detector.ParseMilliseconds,
        detector.CompileMilliseconds,
        inputWidth = dw,
        inputHeight = dh,
        dbCandidateLimit = MaximumLines
    };

    public HipOcrPipeline(Image<Rgb24> source, Arguments options, OcrModels models)
    {
        sw = source.Width; sh = source.Height;
        recWidth = options.Integer("rec-width", 320);
        batchSize = options.Integer("rec-batch", models.DetectorPath is not null ? 4 : 1, maximum: 32);
        int workerCount = options.Integer("rec-workers", models.DetectorPath is not null ? 2 : 1, maximum: 4);
        maxEdge = Math.Clamp((int)Math.Ceiling(Math.Sqrt((double)sw * sw + (double)sh * sh)) + 1, 1, 8192);
        var characters = File.ReadAllLines(models.DictionaryPath, Encoding.UTF8);
        if (characters.Length == 0) throw new InvalidDataException("Empty OCR dictionary.");
        dictionary = new[] { "" }.Concat(characters).Append(" ").ToArray();
        detThreshold = options.Probability("det-threshold", .2f); boxThreshold = options.Probability("box-threshold", .4f); textThreshold = options.Probability("text-threshold", .5f);
        gpu = new HipRuntimeContext(options);
        try
        {
            var native = options.Required("native");
            for (var i = 0; i < workerCount; i++)
                workers.Add(new RecognitionWorker(gpu, native, models.RecognizerPath, recWidth, batchSize, maxEdge, dictionary.Length));
            var shape = Recognizer.OutputShapes.Single().Lengths;
            steps = checked((int)shape[1]);
            if (models.DetectorPath is not null)
            {
                int limit = options.Integer("det-limit", 960);
                double ratio = Math.Min(1d, (double)limit / Math.Max(sw, sh));
                dw = Math.Max(32, (int)Math.Round(sw * ratio / 32) * 32); dh = Math.Max(32, (int)Math.Round(sh * ratio / 32) * 32);
                detector = new HipInferenceSession(gpu, native, models.DetectorPath, "x", dw, dh);
                var detShape = detector.OutputShapes.Single().Lengths;
                if (!detShape.SequenceEqual(new long[] { 1, 1, dh, dw })) throw new InvalidOperationException("Expected a full-resolution [1,1,H,W] DB map.");
                horizontal = gpu.Allocate(checked((long)dw * sh * 3 * sizeof(float))); resized = gpu.Allocate(checked((long)dw * dh * 3));
                int n = checked(dw * dh);
                parents = gpu.Allocate((long)n * sizeof(int)); counts = gpu.Allocate((long)n * sizeof(int));
                components = gpu.Allocate((long)n * sizeof(int)); points = gpu.Allocate((long)n * sizeof(int)); hull = gpu.Allocate((long)n * sizeof(int));
                int blocks = (n + 255) / 256;
                blockRoots = gpu.Allocate((long)blocks * sizeof(int)); blockPoints = gpu.Allocate((long)blocks * sizeof(int));
                offsets = gpu.Allocate(MaximumLines * sizeof(int)); sizes = gpu.Allocate(MaximumLines * sizeof(int));
            }
            (image, pixels) = gpu.Image(source);
            meta = gpu.Allocate(MaximumLines * 6 * sizeof(int)); boxes = gpu.Allocate(MaximumLines * 9 * sizeof(double)); sorted = gpu.Allocate(MaximumLines * 9 * sizeof(double));
            order = gpu.Allocate(MaximumLines * sizeof(int)); control = gpu.Allocate(2 * sizeof(int)); resultCount = gpu.Allocate(sizeof(int));
            tokens = gpu.Allocate(checked((long)MaximumLines * (steps + 1) * sizeof(int))); confidence = gpu.Allocate(MaximumLines * sizeof(float));
            detEvents = Enumerable.Range(0, 5).Select(_ => gpu.Runtime.CreateEvent()).ToArray();
        }
        catch { detector?.Dispose(); foreach (var worker in workers) worker.Dispose(); gpu.Dispose(); throw; }
    }

    public void SetImage(Image<Rgb24> source)
    {
        if (source.Width != sw || source.Height != sh) throw new ArgumentException("OCR session image dimensions changed.");
        source.CopyPixelDataTo(pixels);
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
            // hull is scratch until rectangle decoding, so it can temporarily hold
            // per-pixel point offsets without another full-resolution allocation.
            gpu.Launch("db_scan_local", n, P(parents!), P(counts!), P(components!), P(hull!), P(blockRoots!), P(blockPoints!), I(n));
            gpu.Serial("db_scan_blocks", P(blockRoots!), P(blockPoints!), I((n + 255) / 256), P(control));
            gpu.Launch("db_scan_finalize", n, P(counts!), P(components!), P(hull!), P(blockRoots!), P(blockPoints!), P(offsets!), P(sizes!), I(n));
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
        int batchCount = (lineCount + batchSize - 1) / batchSize;
        try
        {
            for (var wave = 0; wave < batchCount; wave += workers.Count)
            {
                int activeWorkers = Math.Min(workers.Count, batchCount - wave);
                for (var wi = 0; wi < activeWorkers; wi++)
                {
                    int batch = wave + wi, first = batch * batchSize, count = Math.Min(batchSize, lineCount - first);
                    while (recEvents.Count <= batch) recEvents.Add(Enumerable.Range(0, 4).Select(_ => gpu.Runtime.CreateEvent()).ToArray());
                    var worker = workers[wi]; var context = worker.Gpu; var ev = recEvents[batch];
                    ev[0].Record(context.Stream);
                    // Clear unused slots of the final fixed-size batch; never decode them.
                    context.Launch("clear_float", checked(batchSize * recWidth * 48 * 3), P(worker.Session.Input), I(batchSize * recWidth * 48 * 3));
                    for (var slot = 0; slot < count; slot++)
                    {
                        int line = first + slot;
                        Crop(worker, line, dimensions);
                        context.Launch("rec_resize_x", checked(dimensions[line * 6 + 5] * dimensions[line * 6 + 3] * 3), P(worker.Crop), P(worker.Horizontal), P(meta), I(line));
                        context.Launch("rec_resize_tensor", checked(recWidth * 48 * 3), P(worker.Horizontal), P(worker.Session.Input), P(meta), I(recWidth), I(line), I(slot));
                    }
                    ev[1].Record(context.Stream);
                    worker.Enqueue(); ev[2].Record(context.Stream);
                    context.Launch("ctc_argmax", checked(steps * count * 256), P(worker.Session.Outputs[0]), I(dictionary.Length), I(steps), I(count), P(worker.Ids), P(worker.Scores), P(control));
                    context.Launch("ctc_compact", count, P(worker.Ids), P(worker.Scores), I(steps), I(first), I(count), P(tokens), P(confidence));
                    ev[3].Record(context.Stream);
                }
                // Submit all independent streams before waiting. A program/buffer set is
                // reused only after its previous batch and CTC work have completed.
                for (var wi = 0; wi < activeWorkers; wi++) workers[wi].Complete();
                if (artifacts is null) continue;
                Directory.CreateDirectory(artifacts);
                for (var wi = 0; wi < activeWorkers; wi++)
                {
                    int first = (wave + wi) * batchSize, count = Math.Min(batchSize, lineCount - first);
                    var worker = workers[wi];
                    for (var slot = 0; slot < count; slot++)
                    {
                        int line = first + slot, width = dimensions[line * 6 + 2], height = dimensions[line * 6 + 3];
                        Crop(worker, line, dimensions);
                        using var cropImage = Image.LoadPixelData<Rgb24>(worker.Gpu.Read<byte>(worker.Crop, checked(width * height * 3)), width, height);
                        cropImage.SaveAsPng(Path.Combine(artifacts, $"crop-{line:D3}.png"));
                        if (dumpTensors) worker.Session.Dump(Path.Combine(artifacts, $"rec-tensors-{line:D3}"), recWidth, 48, slot);
                    }
                }
            }
        }
        finally { foreach (var worker in workers) worker.Complete(); }
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
        var recognitionSamples = Enumerable.Range(0, batchCount).Select(i => (double)HipEvent.ElapsedTime(recEvents[i][1], recEvents[i][2])).ToArray();
        double detTime = detector is null ? 0 : HipEvent.ElapsedTime(detEvents[2], detEvents[3]), recTime = recognitionSamples.Sum();
        var stages = new Dictionary<string, double[]>
        {
            ["uploadGpu"] = [HipEvent.ElapsedTime(detEvents[0], detEvents[1])],
            ["detectorPreprocessGpu"] = [HipEvent.ElapsedTime(detEvents[1], detEvents[2])],
            ["detectorInferenceGpuWithDeviceCopies"] = [detTime],
            ["detectorPostprocessGpu"] = [HipEvent.ElapsedTime(detEvents[3], detEvents[4])],
            ["inferenceGpuWithDeviceCopiesTotal"] = [detTime + recTime],
            ["recognitionPreprocessGpuTotal"] = [Enumerable.Range(0, batchCount).Sum(i => (double)HipEvent.ElapsedTime(recEvents[i][0], recEvents[i][1]))],
            ["recognitionPostprocessGpuTotal"] = [Enumerable.Range(0, batchCount).Sum(i => (double)HipEvent.ElapsedTime(recEvents[i][2], recEvents[i][3]))],
        };
        if (lineCount > 0) stages["recognitionBatchGpuWithDeviceCopies"] = recognitionSamples;
        if (artifacts is not null && dumpTensors && detector is not null) detector.Dump(Path.Combine(artifacts, "det-tensors"), dw, dh);
        return new(new OcrResult(lines, texts.ToArray(), detTime, recTime), stages);
    }

    private void Crop(RecognitionWorker worker, int line, int[] dimensions)
        => worker.Gpu.Launch("perspective_crop", checked(dimensions[line * 6 + 2] * dimensions[line * 6 + 3] * 3),
            P(image), P(worker.Crop), I(sw), I(sh), P(sorted), I(line), P(meta));

    public void Dispose()
    {
        gpu.Stream.Synchronize();
        foreach (var worker in workers) worker.Dispose();
        foreach (var e in detEvents.Concat(recEvents.SelectMany(e => e))) e.Dispose();
        detector?.Dispose(); gpu.Dispose();
    }

    private sealed class RecognitionWorker : IDisposable
    {
        public HipRuntimeContext Gpu { get; }
        public HipInferenceSession Session { get; }
        public HipDeviceMemory Crop { get; }
        public HipDeviceMemory Horizontal { get; }
        public HipDeviceMemory Ids { get; }
        public HipDeviceMemory Scores { get; }
        private MIGraphXHipAsyncRun? pending;

        public RecognitionWorker(HipRuntimeContext owner, string native, string model, int width, int batch, int edge, int classes)
        {
            Gpu = owner.CreateWorker();
            try
            {
                Session = new HipInferenceSession(Gpu, native, model, "x", width, 48, batch);
                var shape = Session.OutputShapes.Single().Lengths;
                if (shape.Count != 3 || shape[0] != batch || shape[2] != classes)
                    throw new InvalidOperationException("Recognizer must return [rec-batch,time,dictionary classes]. Use an ONNX export with a compatible batch dimension.");
                int steps = checked((int)shape[1]);
                Crop = Gpu.Allocate(checked((long)edge * edge * 3));
                Horizontal = Gpu.Allocate(checked((long)edge * width * 3 * sizeof(float)));
                Ids = Gpu.Allocate(checked((long)steps * batch * sizeof(int)));
                Scores = Gpu.Allocate(checked((long)steps * batch * sizeof(float)));
            }
            catch { Session?.Dispose(); Gpu.Dispose(); throw; }
        }

        public void Enqueue() => pending = Session.Enqueue();
        public void Complete()
        {
            if (pending is null) return;
            pending.Synchronize(); pending.Dispose(); pending = null;
        }
        public void Dispose() { Complete(); Session.Dispose(); Gpu.Dispose(); }
    }
}
