using System.Reflection;
using System.Runtime.InteropServices;
using JYPPX.ROCm.HipSharp;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Modules;
using JYPPX.ROCm.HipSharp.Rtc;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphXSharp;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MIGraphXSharp.Samples.PaddleOcrV6;

/// <summary>One GPU, one ordered stream; reusable allocations and kernels belong to this sample.</summary>
public sealed class HipRuntimeContext : IDisposable
{
    public HipRuntime Runtime { get; }
    public HipStream Stream { get; }
    private readonly HipModule module;
    private readonly Dictionary<string, HipKernel> kernels = [];
    private readonly List<HipDeviceMemory> allocations = [];

    public HipRuntimeContext(Arguments options)
    {
        Runtime = new HipRuntime(options.Has("hip-native") ? options.Required("hip-native") : null);
        try
        {
            Runtime.GetDevice(options.Integer("device", 0, minimum: 0)).MakeCurrent();
            Stream = Runtime.CreateStream();
            try
            {
                if (options.Has("hip-module")) module = Runtime.LoadModule(File.ReadAllBytes(options.Required("hip-module")));
                else
                {
                    var architecture = options.Required("gpu-architecture");
                    if (!System.Text.RegularExpressions.Regex.IsMatch(architecture, "^gfx[0-9a-f]+(:[a-z0-9]+[+-])*$"))
                        throw new ArgumentException("Use a concrete --gpu-architecture, for example gfx1100.");
                    using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("HipKernels.hip")
                        ?? throw new InvalidOperationException("Missing embedded HIP kernel source.");
                    using var reader = new StreamReader(source);
                    var rtc = new HipRtc(options.Has("hiprtc-native") ? options.Required("hiprtc-native") : null);
                    using var program = rtc.CreateProgram(reader.ReadToEnd(), "HipKernels.hip");
                    module = Runtime.LoadModule(program.Compile(["--gpu-architecture=" + architecture, "-std=c++17"]).GetCodeObject());
                }
            }
            catch { Stream.Dispose(); throw; }
        }
        catch { Runtime.Dispose(); throw; }
    }

    public HipDeviceMemory Allocate(long bytes)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        var memory = Runtime.Allocate(checked((ulong)bytes));
        allocations.Add(memory);
        return memory;
    }

    public void Launch(string name, int elements, params HipKernelArgument[] arguments)
    {
        if (elements < 1) return;
        if (!kernels.TryGetValue(name, out var kernel)) kernels[name] = kernel = module.GetKernel(name);
        kernel.Launch(Stream, new HipLaunchDimensions(checked((uint)((elements + 255L) / 256))),
            new HipLaunchDimensions(256), arguments);
    }

    public void Serial(string name, params HipKernelArgument[] arguments)
    {
        if (!kernels.TryGetValue(name, out var kernel)) kernels[name] = kernel = module.GetKernel(name);
        kernel.Launch(Stream, new HipLaunchDimensions(1), new HipLaunchDimensions(1), arguments);
    }

    public static HipKernelArgument P(HipDeviceMemory memory) => HipKernelArgument.DevicePointer(memory);
    public static HipKernelArgument I(int value) => HipKernelArgument.Scalar32(value);
    // HipSharp's current scalar ABI is int32. The kernels explicitly reinterpret float bits.
    public static HipKernelArgument F(float value) => I(BitConverter.SingleToInt32Bits(value));

    public T[] Read<T>(HipDeviceMemory memory, int elements) where T : unmanaged
    {
        if (elements == 0) return [];
        if (elements < 0) throw new ArgumentOutOfRangeException(nameof(elements));
        var result = new T[elements];
        var bytes = new byte[checked(elements * Marshal.SizeOf<T>())];
        memory.CopyToAsync(bytes, Stream);
        Stream.Synchronize();
        MemoryMarshal.Cast<byte, T>(bytes).CopyTo(result);
        return result;
    }

    public void Write<T>(HipDeviceMemory memory, T[] data) where T : unmanaged
        => memory.CopyFromAsync(MemoryMarshal.AsBytes(data.AsSpan()).ToArray(), Stream);

    public (HipDeviceMemory Memory, byte[] Pixels) Image(Image<Rgb24> image)
    {
        var bytes = new byte[checked(image.Width * image.Height * 3)];
        image.CopyPixelDataTo(bytes);
        return (Allocate(bytes.Length), bytes);
    }

    public void Resize(HipDeviceMemory source, int sourceWidth, int sourceHeight, HipDeviceMemory horizontal,
        HipDeviceMemory destination, int width, int height, int left = 0, int top = 0, int cropSize = 0, bool cubic = false)
    {
        var cropWidth = cropSize == 0 ? sourceWidth : cropSize;
        var cropHeight = cropSize == 0 ? sourceHeight : cropSize;
        Launch("resize_x", checked(width * cropHeight * 3), P(source), P(horizontal),
            I(sourceWidth), I(sourceHeight), I(left), I(top), I(cropWidth), I(cropHeight), I(width), I(cubic ? 1 : 0));
        Launch("resize_y", checked(width * height * 3), P(horizontal), P(destination),
            I(width), I(cropHeight), I(height), I(cubic ? 1 : 0));
    }

    public void Dispose()
    {
        // Do not release live GPU buffers if synchronization fails.
        Stream.Synchronize();
        for (var index = allocations.Count - 1; index >= 0; index--) allocations[index].Dispose();
        module.Dispose();
        Stream.Dispose();
        Runtime.Dispose();
    }
}

/// <summary>Static model with persistent device input/output; host snapshots are diagnostic only.</summary>
public sealed class HipInferenceSession : IDisposable
{
    private readonly MIGraphXProgram program;
    private readonly HipRuntimeContext gpu;
    private readonly MIGraphXHipDeviceInput[] inputs;
    public HipDeviceMemory Input { get; }
    public HipDeviceMemory[] Outputs { get; }
    public MIGraphXShape[] OutputShapes { get; }
    public double ParseMilliseconds { get; }
    public double CompileMilliseconds { get; }
    public string ModelSha256 { get; }

    public HipInferenceSession(HipRuntimeContext gpu, string native, string model, string inputName, int width, int height)
    {
        this.gpu = gpu;
        ModelSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(model))).ToLowerInvariant();
        using var options = new MIGraphXOnnxOptions(native);
        options.SetInputParameterShape(inputName, new long[] { 1, 3, height, width });
        var timer = System.Diagnostics.Stopwatch.StartNew();
        program = MIGraphXProgram.ParseOnnxFile(model, options);
        ParseMilliseconds = timer.Elapsed.TotalMilliseconds;
        try
        {
            using var target = new MIGraphXTarget(native, "gpu");
            using var compile = new MIGraphXCompileOptions(native, offloadCopy: false);
            timer.Restart();
            program.Compile(target, compile);
            CompileMilliseconds = timer.Elapsed.TotalMilliseconds;
            var shapes = program.GetParameterShapes();
            if (shapes.Count != 1 || !shapes.TryGetValue(inputName, out var shape) ||
                shape.DataType != MIGraphXShapeDataType.Float32 || !shape.Lengths.SequenceEqual(new long[] { 1, 3, height, width }))
                throw new InvalidOperationException("Expected exactly one static float32 NCHW input.");
            Input = gpu.Allocate(shape.ByteCount);
            inputs = [new MIGraphXHipDeviceInput(inputName, shape, Input)];
            OutputShapes = program.GetOutputShapes().ToArray();
            if (OutputShapes.Any(s => s.IsDynamic || !s.IsStandard || !s.IsPacked || s.DataType != MIGraphXShapeDataType.Float32))
                throw new InvalidOperationException("The vision kernels require static packed float32 outputs.");
            Outputs = OutputShapes.Select(s => gpu.Allocate(s.ByteCount)).ToArray();
        }
        catch { program.Dispose(); throw; }
    }

    public MIGraphXHipAsyncRun Enqueue() => program.RunDeviceIntoAsync(inputs, Outputs, gpu.Stream);

    public void Dump(string directory, int width, int height)
    {
        Directory.CreateDirectory(directory);
        var input = gpu.Read<float>(Input, checked(width * height * 3));
        File.WriteAllBytes(Path.Combine(directory, "input.f32"), MemoryMarshal.AsBytes(input.AsSpan()).ToArray());
        for (var index = 0; index < Outputs.Length; index++)
            File.WriteAllBytes(Path.Combine(directory, $"output-{index}.f32"),
                MemoryMarshal.AsBytes(gpu.Read<float>(Outputs[index], checked((int)OutputShapes[index].ElementCount)).AsSpan()).ToArray());
        JsonOutput.Write(Path.Combine(directory, "tensors.json"), new { modelSha256 = ModelSha256,
            inputShape = new[] { 1, 3, height, width }, outputs = OutputShapes.Select((s, index) => new { index, shape = s.Lengths }) });
    }

    public void Dispose() { gpu.Stream.Synchronize(); program.Dispose(); }
}

