using JYPPX.ROCm.MIGraphXSharp;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using JYPPX.ROCm.HipSharp;
using System.Security.Cryptography;

internal static class RuntimeRegression
{
    internal static void Run(string native, string hipPath, string model, string output, Action<string, Action> check, Action<MIGraphXProgram, bool> compile)
    {
        var values = new[] { 0.25f, -1f, 2f, 9f };
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        MIGraphXProgram Parse() { using var options = new MIGraphXOnnxOptions(native); return MIGraphXProgram.ParseOnnxFile(model, options); }
        void RunIdentity(MIGraphXProgram program, int batch = 1)
        {
            using var input = MIGraphXArgument.Create(native, new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { batch, 4 }), Enumerable.Range(0, batch).SelectMany(_ => values).ToArray());
            using var parameters = new MIGraphXParameterMap(native); parameters.Add("input", input);
            using var result = program.Run(parameters);
            Equal(Enumerable.Range(0, batch).SelectMany(_ => values).ToArray(), result[0].ToArray<float>());
        }
        check("argument-persistence-clones-generators", () =>
        {
            using var argument = MIGraphXArgument.Create(native, shape, values);
            using var clone = argument.Clone();
            var path = Path.Combine(output, "argument.mxr"); argument.Save(path);
            using var loaded = MIGraphXArgument.Load(native, path);
            Equal(values, loaded.ToArray<float>());
            if (!argument.HasSameNativeContent(clone) || !argument.HasSameNativeContent(loaded)) throw new Exception("Argument equality changed.");
            using var first = MIGraphXArgument.Generate(native, shape, 42);
            using var second = MIGraphXArgument.Generate(native, shape, 42);
            Equal(first.ToArray<float>(), second.ToArray<float>());
            using var empty = MIGraphXArgument.CreateEmpty(native, shape);
            if (empty.ToArray<float>().Length != 4) throw new Exception("Argument factory shape mismatch.");
        });
        check("gpu-dynamic-two-batches-and-rejections", () =>
        {
            using var options = new MIGraphXOnnxOptions(native);
            options.SetDynamicInputParameterShape("input", new[] { new MIGraphXDynamicDimension(1, 2), new MIGraphXDynamicDimension(4) });
            using var program = MIGraphXProgram.ParseOnnxFile(model, options);
            compile(program, true); RunIdentity(program, 1); RunIdentity(program, 2);
            using var invalid = MIGraphXArgument.Create(native, new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 3, 4 }), new float[12]);
            using var parameters = new MIGraphXParameterMap(native); parameters.Add("input", invalid);
            Expect<ArgumentException>(() => { using var result = program.Run(parameters); });
        });
        check("gpu-cache-miss-hit-corruption-and-option-identity", () =>
        {
            using var fileOptions = new MIGraphXFileOptions(native);
            var root = Path.Combine(output, "cache-" + Guid.NewGuid().ToString("N"));
            var cache = new MIGraphXModelCache(root);
            string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            var metadata = new MIGraphXCacheMetadata(Hash(model), "gpu", "offloadCopy=true", "msgpack", Hash(native));
            var builds = 0;
            MIGraphXProgram Builder() { builds++; return Parse(); }
            using (var cold = cache.GetOrBuild(metadata, fileOptions, Builder))
            { if (cold.Kind != MIGraphXCacheLookupKind.Rebuilt || cold.PreviousLookup != MIGraphXCacheLookupKind.Miss) throw new Exception("Expected cold miss."); compile(cold.Program, true); RunIdentity(cold.Program); }
            using (var warm = cache.GetOrBuild(metadata, fileOptions, Builder))
            { if (warm.Kind != MIGraphXCacheLookupKind.Hit || builds != 1) throw new Exception("Expected warm hit."); compile(warm.Program, true); RunIdentity(warm.Program); }
            File.WriteAllText(Path.Combine(root, metadata.ComputeKey() + ".migraphx"), "corrupted");
            using var rebuilt = cache.GetOrBuild(metadata, fileOptions, Builder);
            if (rebuilt.PreviousLookup != MIGraphXCacheLookupKind.Corrupt || builds != 2) throw new Exception("Corrupt cache was not rebuilt.");
            compile(rebuilt.Program, true); RunIdentity(rebuilt.Program);
            var changed = new MIGraphXCacheMetadata(Hash(model), "gpu", "offloadCopy=false", "msgpack", Hash(native));
            using var miss = cache.GetOrBuild(changed, fileOptions, Builder);
            if (miss.Kind != MIGraphXCacheLookupKind.Rebuilt || miss.Key == rebuilt.Key) throw new Exception("Changed options reused cache.");
        });
        check("gpu-dynamic-save-load-cache-and-range-validation", () =>
        {
            var ranges = new[] { new MIGraphXDynamicDimension(1, 2), new MIGraphXDynamicDimension(4) };
            var hints = new[] { new MIGraphXCacheOverride("input", ranges) };
            using var options = new MIGraphXOnnxOptions(native); options.SetDynamicInputParameterShape("input", ranges);
            using var original = MIGraphXProgram.ParseOnnxFile(model, options);
            using var files = new MIGraphXFileOptions(native);
            var path = Path.Combine(output, "dynamic.mxr"); original.Save(path, files);
            using (var loaded = MIGraphXProgram.Load(path, files, hints)) { compile(loaded, true); RunIdentity(loaded, 2); }
            Expect<InvalidOperationException>(() => { using var invalid = MIGraphXProgram.Load(path, files,
                new[] { new MIGraphXCacheOverride("input", new[] { new MIGraphXDynamicDimension(1, 3), new MIGraphXDynamicDimension(4) }) }); });
            var cache = new MIGraphXModelCache(Path.Combine(output, "dynamic-cache-" + Guid.NewGuid().ToString("N")));
            var metadata = new MIGraphXCacheMetadata(MIGraphXCacheMetadata.ComputeModelSha256(File.ReadAllBytes(model)), "gpu", "dynamic", "msgpack", MIGraphXCacheMetadata.ComputeNativeFingerprint(native), hints);
            using (var cold = cache.GetOrBuild(metadata, files, original.Clone)) { compile(cold.Program, true); RunIdentity(cold.Program, 1); }
            using var warm = cache.GetOrBuild(metadata, files, () => throw new Exception("Dynamic cache missed."));
            if (warm.Kind != MIGraphXCacheLookupKind.Hit) throw new Exception("Dynamic cache did not hit.");
            compile(warm.Program, true); RunIdentity(warm.Program, 2);
        });
        check("gpu-pending-guards-early-dispose-and-stream-isolation", () =>
        {
            using var hip = new HipRuntime(hipPath);
            using var first = hip.CreateStream(); using var second = hip.CreateStream();
            using var program = Parse(); compile(program, true);
            using var argument = MIGraphXArgument.Create(native, shape, values);
            var parameters = new MIGraphXParameterMap(native); parameters.Add("input", argument);
            using var a = program.RunHostAsync(parameters, first);
            using var b = program.RunHostAsync(parameters, second);
            parameters.Dispose();
            Expect<InvalidOperationException>(() => { using var copy = program.Clone(); });
            Expect<InvalidOperationException>(program.Sort);
            Expect<InvalidOperationException>(() => compile(program, true));
            a.Synchronize(); Equal(values, a.Outputs[0].ToArray<float>());
            Expect<InvalidOperationException>(() => { using var copy = program.Clone(); });
            b.Synchronize(); Equal(values, b.Outputs[0].ToArray<float>());
            using var after = program.Clone();
            var disposedProgram = Parse(); compile(disposedProgram, false);
            var device = hip.Allocate(16); var destination = hip.Allocate(16);
            var bytes = new byte[16]; Buffer.BlockCopy(values, 0, bytes, 0, 16); device.CopyFrom(bytes);
            using var pending = disposedProgram.RunDeviceIntoAsync(new[] { new MIGraphXHipDeviceInput("input", shape, device) }, new[] { destination }, first);
            device.Dispose(); destination.Dispose(); disposedProgram.Dispose();
            pending.Synchronize();
            if (!pending.IsCompleted) throw new Exception("Early disposal did not complete safely.");
        });
        check("gpu-context-retains-program-and-invalidates-revision", () =>
        {
            var program = Parse(); compile(program, true);
            using var context = program.GetExperimentalContext();
            context.Finish();
            program.Sort(); Expect<InvalidOperationException>(context.Finish);
            compile(program, true);
            using var retained = program.GetExperimentalContext();
            program.Dispose(); retained.Finish();
        });
        check("gpu-int64-tensor-values", () =>
        {
            using var program = new MIGraphXProgram(native); using var main = program.GetMainModule();
            var intShape = new MIGraphXShape(MIGraphXShapeDataType.Int64, new long[] { 4 });
            using var inputInstruction = main.AddParameter("tokens", intShape);
            using var returns = new MIGraphXInstructions(native, new[] { inputInstruction }); using var returned = main.AddReturn(returns);
            compile(program, true);
            var expected = new[] { 1L, 2147483648L, -7L, 9007199254740991L };
            using var argument = MIGraphXArgument.Create(native, intShape, expected);
            using var parameters = new MIGraphXParameterMap(native); parameters.Add("tokens", argument);
            using var result = program.Run(parameters);
            if (!expected.SequenceEqual(result[0].ToArray<long>())) throw new Exception("Int64 values changed.");
        });
        var tensorflow = Path.Combine(Path.GetDirectoryName(model)!, "tensorflow-quantization", "m12-tensorflow-minimal.pb");
        if (MIGraphXCapabilities.IsSupported(native, MIGraphXFeature.TensorFlow)) check("gpu-tensorflow-file-buffer-and-options-clone", () =>
        {
            using var options = new MIGraphXTfOptions(native); options.SetInputParameterShape("input", new long[] { 1, 4 }); options.SetOutputNames(new[] { "output" });
            using var clone = options.Clone();
            using var file = MIGraphXProgram.ParseTfFile(tensorflow, clone); compile(file, true); RunIdentity(file);
            using var buffer = MIGraphXProgram.ParseTfBuffer(File.ReadAllBytes(tensorflow), clone); compile(buffer, true); RunIdentity(buffer);
        });
        foreach (var mode in new[] { "fp16", "bf16", "int8", "fp8" }) check("gpu-calibrated-dot-" + mode, () =>
        {
            using var program = CreateDot(native);
            using var target = new MIGraphXTarget(native);
            var dotShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 32, 32 });
            using var inputs = DotParameters(native, dotShape);
            if (mode == "fp16") program.QuantizeFp16();
            if (mode == "bf16") program.QuantizeBf16();
            if (mode == "int8")
            {
                var options = new MIGraphXQuantizeInt8Options(native); options.AddOpName("dot"); options.AddOpName("convolution");
                using (var calibration = DotParameters(native, dotShape)) options.AddCalibrationData(calibration);
                using var clone = options.Clone(); options.Dispose();
                program.QuantizeInt8(target, clone);
            }
            if (mode == "fp8")
            {
                var options = new MIGraphXQuantizeFp8Options(native);
                using (var calibration = DotParameters(native, dotShape)) options.AddCalibrationData(calibration);
                using var clone = options.Clone(); options.Dispose();
                program.QuantizeFp8(target, clone);
            }
            compile(program, true);
            using var results = program.Run(inputs);
            var (left, right) = DotValues();
            var expected = new float[32 * 32];
            for (var row = 0; row < 32; row++) for (var column = 0; column < 32; column++)
                for (var k = 0; k < 32; k++) expected[row * 32 + column] += left[row * 32 + k] * right[k * 32 + column];
            Equal(expected, results[0].ToArray<float>(), mode is "int8" or "fp8" ? 0.25f : 0.001f);
        });
        CallbackRegression.Run(native, hipPath, output, check);
    }

    private static MIGraphXProgram CreateDot(string native)
    {
        var program = new MIGraphXProgram(native);
        try
        {
            using var main = program.GetMainModule(); var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 32, 32 });
            using var a = main.AddParameter("a", shape); using var b = main.AddParameter("b", shape);
            using var args = new MIGraphXInstructions(native, new[] { a, b }); using var dot = MIGraphXOperation.Create(native, "dot");
            using var instruction = main.AddInstruction(dot, args); using var returns = new MIGraphXInstructions(native, new[] { instruction }); using var returned = main.AddReturn(returns);
            return program;
        }
        catch { program.Dispose(); throw; }
    }
    private static MIGraphXParameterMap DotParameters(string native, MIGraphXShape shape)
    {
        var parameters = new MIGraphXParameterMap(native);
        try
        {
            var (left, right) = DotValues();
            using var a = MIGraphXArgument.Create(native, shape, left);
            using var b = MIGraphXArgument.Create(native, shape, right);
            parameters.Add("a", a); parameters.Add("b", b); return parameters;
        }
        catch { parameters.Dispose(); throw; }
    }
    private static (float[], float[]) DotValues()
    {
        var left = new float[32 * 32]; var right = new float[32 * 32];
        for (var row = 0; row < 32; row++) for (var column = 0; column < 32; column++)
        { left[row * 32 + column] = column % 4 + 1; right[row * 32 + column] = row == column ? 2 : row == (column + 1) % 32 ? 1 : 0; }
        return (left, right);
    }
    internal static void Equal(float[] expected, float[] actual, float tolerance = 1e-5f)
    {
        if (expected.Length != actual.Length || expected.Where((value, index) => !float.IsFinite(actual[index]) || Math.Abs(value - actual[index]) > tolerance).Any())
            throw new Exception("Numerical mismatch: " + string.Join(",", actual) + "; expected " + string.Join(",", expected));
    }
    internal static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
