using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JYPPX.ROCm.HipSharp;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using JYPPX.ROCm.MIGraphXSharp;

namespace M11RuntimeProbe;

internal static class Program
{
    private const string Evidence = "runtime-candidate-executed-review-required";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        ProbeOptions? options = null;
        ProbeReport? report = null;
        try
        {
            options = ProbeOptions.Parse(args);
            report = new ProbeReport(options.Phase, options.SourceSha, CaptureIdentity(options));
            var runner = new ProbeRunner(options, report);
            if (string.Equals(options.Phase, "functional", StringComparison.Ordinal))
            {
                await runner.RunFunctionalAsync();
            }
            else if (string.Equals(options.Phase, "cache-restart", StringComparison.Ordinal))
            {
                await runner.RunCacheRestartAsync();
            }
            else
            {
                throw new ArgumentException("Phase must be 'functional' or 'cache-restart'.", nameof(args));
            }
            report.Complete();
        }
        catch (Exception exception)
        {
            report ??= new ProbeReport(options?.Phase ?? "argument-validation", options?.SourceSha ?? "unknown", null);
            report.Fail(exception);
        }

        var json = JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;
        if (options is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
            File.WriteAllText(options.OutputPath, json, new UTF8Encoding(false));
        }
        Console.Write(json);
        return report?.State == "executed" ? 0 : 1;
    }

    private static ManagedIdentity CaptureIdentity(ProbeOptions options)
    {
        if (!string.Equals(MIGraphXBuildInfo.PackageVersion, options.ExpectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Core package version is {MIGraphXBuildInfo.PackageVersion}, expected {options.ExpectedVersion}.");
        }

        var core = typeof(MIGraphXBuildInfo).Assembly;
        var adapter = typeof(MIGraphXHipExecution).Assembly;
        var hip = typeof(HipRuntime).Assembly;
        var informational = core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        if (!informational.Contains(options.SourceSha, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The core assembly informational version is not bound to the requested source SHA.");
        }
        return new ManagedIdentity(
            AssemblyIdentity(core),
            AssemblyIdentity(adapter),
            AssemblyIdentity(hip),
            options.ExpectedVersion,
            informational);
    }

    private static AssemblyIdentity AssemblyIdentity(Assembly assembly)
    {
        var path = assembly.Location;
        return new AssemblyIdentity(
            assembly.GetName().Name ?? string.Empty,
            assembly.GetName().Version?.ToString() ?? string.Empty,
            Path.GetFileName(path),
            HashFile(path));
    }

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed class ProbeRunner
{
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(120);
    private static readonly float[] IdentityInput = [0.25f, -1f, 2f, 9f];
    private readonly ProbeOptions options;
    private readonly ProbeReport report;
    private readonly string identityPath;
    private readonly string multiOutputPath;
    private readonly string dynamicIdentityPath;
    private readonly string cacheRoot;
    private readonly byte[] identityModel;
    private readonly byte[] multiOutputModel;
    private readonly byte[] dynamicIdentityModel;

    internal ProbeRunner(ProbeOptions options, ProbeReport report)
    {
        this.options = options;
        this.report = report;
        identityPath = Fixture("m2-identity-float32.onnx", "0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9");
        multiOutputPath = Fixture("m11-multi-output-float32.onnx", "0c703e1455a35c103496ff8173a1c7f2738c878cb394aba83e2934e06008bb9a");
        dynamicIdentityPath = Fixture("m11-dynamic-identity-float32.onnx", "6856f3da340b053cace6042544f03c8bb75812eafb82f7cc8ee2a41d0327981d");
        identityModel = File.ReadAllBytes(identityPath);
        multiOutputModel = File.ReadAllBytes(multiOutputPath);
        dynamicIdentityModel = File.ReadAllBytes(dynamicIdentityPath);
        cacheRoot = Path.GetFullPath(Path.Combine(options.RecordDirectory, "cache"));
        var recordPrefix = options.RecordDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? options.RecordDirectory
            : options.RecordDirectory + Path.DirectorySeparatorChar;
        if (!cacheRoot.StartsWith(recordPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The cache root must be a child of the current evidence record.");
        }
    }

    internal async Task RunFunctionalAsync()
    {
        Directory.CreateDirectory(cacheRoot);
        await Run("m11-registry-before", () =>
        {
            var names = MIGraphXOnnxWorkflow.GetRegisteredOperators(options.NativePath);
            var processMap = CaptureProcessMap();
            return Detail(("count", names.Count), ("orderedJsonSha256", Program.HashBytes(JsonSerializer.SerializeToUtf8Bytes(names))), ("processMapSha256", processMap.Sha256), ("nativeMapEntries", processMap.NativeEntries));
        });
        await Run("m4-explicit-lifecycle", 3, _ =>
        {
            using var target = new MIGraphXTarget(options.NativePath);
            using var program = new MIGraphXProgram(options.NativePath);
            using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
            using var compileOptions = new MIGraphXCompileOptions(options.NativePath);
            Require(!program.IsCompiled, "A new program must be uncompiled.");
            return Detail(("disposedDeterministically", true));
        });
        await Run("m4-file-buffer-reference", 3, _ =>
        {
            var fromFile = RunIdentity(identityPath, null, IdentityInput);
            var fromBuffer = RunIdentity(null, identityModel, IdentityInput);
            Require(fromFile.Output.SequenceEqual(IdentityInput), "File Identity reference mismatch.");
            Require(fromBuffer.Output.SequenceEqual(IdentityInput), "Buffer Identity reference mismatch.");
            using var firstOptions = new MIGraphXOnnxOptions(options.NativePath);
            using var secondOptions = new MIGraphXOnnxOptions(options.NativePath);
            using var first = MIGraphXProgram.ParseOnnxFile(identityPath, firstOptions);
            using var second = MIGraphXProgram.ParseOnnxBuffer(identityModel, secondOptions);
            Require(first.HasSameNativeContent(second), "Independent file/buffer parses differ in native program content.");
            return Detail(("inputName", fromFile.InputName), ("shape", fromFile.Shape), ("file", fromFile.Output), ("buffer", fromBuffer.Output));
        });
        await Run("m4-multi-output-order-lifetime", 3, _ =>
        {
            var input = new[] { 1f, -2f, 3.5f, -4f };
            var expectedNeg = new[] { -1f, 2f, -3.5f, 4f };
            float[][] copied;
            using (var program = ParseCompile(multiOutputModel, offloadCopy: true))
            {
                var parameter = program.GetParameterShapes().Single();
                Require(parameter.Value.Lengths.SequenceEqual(new long[] { 2, 2 }), "Unexpected multi-output input shape.");
                using var argument = MIGraphXArgument.Create(options.NativePath, parameter.Value, input);
                using var independent = MIGraphXArgument.Create(options.NativePath, parameter.Value, input);
                Require(argument.HasSameNativeContent(independent), "Independent host arguments differ in native content.");
                using var map = new MIGraphXParameterMap(options.NativePath);
                map.Add(parameter.Key, argument);
                using var outputs = program.Run(map);
                Require(outputs.Count == 2, "The multi-output fixture did not return exactly two outputs.");
                Require(outputs[0].Shape.Lengths.SequenceEqual(new long[] { 2, 2 }), "First output shape mismatch.");
                Require(outputs[1].Shape.Lengths.SequenceEqual(new long[] { 2, 2 }), "Second output shape mismatch.");
                copied = [outputs[0].ToArray<float>(), outputs[1].ToArray<float>()];
            }
            Require(copied[0].SequenceEqual(input), "Ordered Identity output mismatch after owner disposal.");
            Require(copied[1].SequenceEqual(expectedNeg), "Ordered Neg output mismatch after owner disposal.");
            return Detail(("outputCount", copied.Length), ("identity", copied[0]), ("neg", copied[1]), ("independentAfterDispose", true));
        });
        await Run("m4-dispose-and-input-negatives", () =>
        {
            using var optionsOwner = new MIGraphXOnnxOptions(options.NativePath);
            using var parsed = MIGraphXProgram.ParseOnnxBuffer(identityModel, optionsOwner);
            var parameter = parsed.GetParameterShapes().Single();
            using var argument = MIGraphXArgument.Create(options.NativePath, parameter.Value, IdentityInput);
            using var wrongMap = new MIGraphXParameterMap(options.NativePath);
            wrongMap.Add("wrong-input", argument);
            Expect<InvalidOperationException>(() => parsed.Run(wrongMap), "uncompiled Run");
            using var target = new MIGraphXTarget(options.NativePath);
            using var compileOptions = new MIGraphXCompileOptions(options.NativePath);
            parsed.Compile(target, compileOptions);
            Expect<ArgumentException>(() => parsed.Run(wrongMap), "wrong input name");
            Expect<ArgumentException>(() => MIGraphXArgument.Create(options.NativePath, parameter.Value, new[] { 1f }), "shape/value count mismatch");
            var disposedProgram = new MIGraphXProgram(options.NativePath);
            disposedProgram.Dispose();
            Expect<ObjectDisposedException>(() => _ = disposedProgram.IsCompiled, "program post-Dispose access");
            var disposedArgument = MIGraphXArgument.Create(options.NativePath, parameter.Value, IdentityInput);
            disposedArgument.Dispose();
            Expect<ObjectDisposedException>(() => disposedArgument.ToArray<float>(), "argument post-Dispose access");
            return Detail(("managedRejections", 5));
        });
        await Run("m5-static-overrides", 3, _ =>
        {
            var first = RunStaticOverride([1, 4], IdentityInput);
            var secondInput = new[] { 1f, 2f, 3f, 4f, -1f, -2f, -3f, -4f };
            var second = RunStaticOverride([2, 4], secondInput);
            Require(first.SequenceEqual(IdentityInput) && second.SequenceEqual(secondInput), "Static override reference mismatch.");
            return Detail(("acceptedShapes", new[] { "[1,4]", "[2,4]" }));
        });
        await Run("m5-dynamic-overrides", 3, _ =>
        {
            using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
            onnxOptions.SetDynamicInputParameterShape("input", [MIGraphXDynamicDimension.Range(1, 2, [1, 2]), MIGraphXDynamicDimension.Fixed(4)]);
            using var program = MIGraphXProgram.ParseOnnxBuffer(dynamicIdentityModel, onnxOptions);
            using var target = new MIGraphXTarget(options.NativePath);
            using var compileOptions = new MIGraphXCompileOptions(options.NativePath);
            program.Compile(target, compileOptions);
            var first = RunConcrete(program, "input", [1, 4], IdentityInput);
            var secondInput = new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f };
            var second = RunConcrete(program, "input", [2, 4], secondInput);
            Require(first.SequenceEqual(IdentityInput) && second.SequenceEqual(secondInput), "Dynamic override reference mismatch.");
            return Detail(("acceptedShapes", new[] { "[1,4]", "[2,4]" }), ("completion", "synchronous-run"));
        });
        await Run("m5-invalid-range-name-shape", () =>
        {
            Expect<ArgumentOutOfRangeException>(() => new MIGraphXDynamicDimension(2, 1), "invalid dynamic range");
            using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
            onnxOptions.SetDynamicInputParameterShape("input", [MIGraphXDynamicDimension.Range(1, 2), MIGraphXDynamicDimension.Fixed(4)]);
            using var program = MIGraphXProgram.ParseOnnxBuffer(dynamicIdentityModel, onnxOptions);
            using var target = new MIGraphXTarget(options.NativePath);
            using var compileOptions = new MIGraphXCompileOptions(options.NativePath);
            program.Compile(target, compileOptions);
            using var wrongNameArgument = MIGraphXArgument.Create(options.NativePath, new MIGraphXShape(MIGraphXShapeDataType.Float32, [1, 4]), IdentityInput);
            using var wrongNameMap = new MIGraphXParameterMap(options.NativePath);
            wrongNameMap.Add("wrong-input", wrongNameArgument);
            Expect<ArgumentException>(() => program.Run(wrongNameMap), "dynamic wrong input name");
            Expect<MIGraphXException>(() => RunConcrete(program, "input", [3, 4], Enumerable.Range(0, 12).Select(value => (float)value).ToArray()), "out-of-range dynamic input");
            return Detail(("managedRejections", 2), ("nativeRangeRejections", 1));
        });
        await Run("m5-save-load-recompile", 3, iteration =>
        {
            var savePath = Path.Combine(options.RecordDirectory, "saved", $"identity-{iteration}.msgpack");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
            using var original = MIGraphXProgram.ParseOnnxBuffer(identityModel, onnxOptions);
            using var fileOptions = new MIGraphXFileOptions(options.NativePath);
            original.Save(savePath, fileOptions);
            using var loaded = MIGraphXProgram.Load(savePath, fileOptions);
            Compile(loaded, offloadCopy: true);
            var output = RunCompiledIdentity(loaded, IdentityInput);
            Require(output.SequenceEqual(IdentityInput), "Loaded program reference mismatch.");
            return Detail(("format", fileOptions.FileFormat), ("payloadSha256", Program.HashFile(savePath)), ("referenceMatched", true));
        });
        await Run("m5-cache-cold-warm-identity", 3, iteration =>
        {
            using var fileOptions = new MIGraphXFileOptions(options.NativePath);
            var cache = new MIGraphXModelCache(cacheRoot);
            var compileIdentity = iteration == 0 ? "offloadCopy=true" : $"offloadCopy=true;iteration={iteration}";
            var metadata = CacheMetadata(identityModel, compileIdentity);
            using var cold = cache.GetOrBuild(metadata, fileOptions, BuildIdentity);
            Require(cold.Kind == MIGraphXCacheLookupKind.Rebuilt && cold.PreviousLookup == MIGraphXCacheLookupKind.Miss, "Cold cache did not rebuild from a miss.");
            Compile(cold.Program, offloadCopy: true);
            Require(RunCompiledIdentity(cold.Program, IdentityInput).SequenceEqual(IdentityInput), "Cold cache output mismatch.");
            using var warm = cache.GetOrBuild(metadata, fileOptions, () => throw new InvalidOperationException("Warm cache unexpectedly invoked builder."));
            Require(warm.Kind == MIGraphXCacheLookupKind.Hit, "Warm cache did not hit.");
            Compile(warm.Program, offloadCopy: true);
            Require(RunCompiledIdentity(warm.Program, IdentityInput).SequenceEqual(IdentityInput), "Warm cache output mismatch.");
            return CacheDetail(metadata, cold.Kind, warm.Kind);
        });
        await Run("m5-cache-model-options-miss", () =>
        {
            using var fileOptions = new MIGraphXFileOptions(options.NativePath);
            var cache = new MIGraphXModelCache(cacheRoot);
            var changedModel = CacheMetadata(multiOutputModel, "offloadCopy=true");
            using var modelMiss = cache.GetOrBuild(changedModel, fileOptions, BuildMultiOutput);
            Require(modelMiss.PreviousLookup == MIGraphXCacheLookupKind.Miss, "Changed model identity did not miss.");
            var changedOptions = CacheMetadata(identityModel, "offloadCopy=false");
            using var optionsMiss = cache.GetOrBuild(changedOptions, fileOptions, BuildIdentity);
            Require(optionsMiss.PreviousLookup == MIGraphXCacheLookupKind.Miss, "Changed compile-options identity did not miss.");
            return Detail(("modelKey", changedModel.ComputeKey()), ("optionsKey", changedOptions.ComputeKey()));
        });
        await Run("m5-cache-corruption-rebuild", () =>
        {
            using var fileOptions = new MIGraphXFileOptions(options.NativePath);
            var cache = new MIGraphXModelCache(cacheRoot);
            var metadata = CacheMetadata(identityModel, "offloadCopy=true");
            var payload = Path.Combine(cacheRoot, metadata.ComputeKey() + ".migraphx");
            var sidecar = Path.Combine(cacheRoot, metadata.ComputeKey() + ".json");
            File.AppendAllText(payload, "payload-corruption", Encoding.UTF8);
            using (var repairedPayload = cache.GetOrBuild(metadata, fileOptions, BuildIdentity))
            {
                Require(repairedPayload.PreviousLookup == MIGraphXCacheLookupKind.Corrupt, "Payload corruption did not fail closed.");
            }
            File.WriteAllText(sidecar, "{\"corrupt\":true}", new UTF8Encoding(false));
            using (var repairedMetadata = cache.GetOrBuild(metadata, fileOptions, BuildIdentity))
            {
                Require(repairedMetadata.PreviousLookup == MIGraphXCacheLookupKind.Corrupt, "Metadata corruption did not fail closed.");
                Compile(repairedMetadata.Program, offloadCopy: true);
                Require(RunCompiledIdentity(repairedMetadata.Program, IdentityInput).SequenceEqual(IdentityInput), "Corruption rebuild output mismatch.");
            }
            return Detail(("payloadSha256", Program.HashFile(payload)), ("metadataSha256", Program.HashFile(sidecar)));
        });
        await Run("m5-cache-concurrent-writers", async () =>
        {
            using var fileOptions = new MIGraphXFileOptions(options.NativePath);
            var cache = new MIGraphXModelCache(cacheRoot);
            var metadata = CacheMetadata(identityModel, "offloadCopy=true;case=concurrent");
            var builds = 0;
            MIGraphXProgram Builder()
            {
                Interlocked.Increment(ref builds);
                Thread.Sleep(25);
                return BuildIdentity();
            }
            var results = await Task.WhenAll(
                Task.Run(() => cache.GetOrBuild(metadata, fileOptions, Builder)),
                Task.Run(() => cache.GetOrBuild(metadata, fileOptions, Builder)));
            try
            {
                Require(builds == 1, $"Concurrent cache builders executed {builds} times.");
                Require(results.Any(value => value.Kind == MIGraphXCacheLookupKind.Rebuilt), "No concurrent caller rebuilt.");
                Require(results.Any(value => value.Kind == MIGraphXCacheLookupKind.Hit), "No concurrent caller hit.");
            }
            finally
            {
                foreach (var result in results) result.Dispose();
            }
            Require(Directory.GetFiles(cacheRoot, "*.tmp").Length == 0, "Cache temporary files remain.");
            return Detail(("builderCalls", builds), ("temporaryFiles", 0));
        });
        await Run("m6-host-async-completion", 3, _ =>
        {
            using var hip = new HipRuntime(options.HipPath);
            using var stream = hip.CreateStream();
            using var program = ParseCompile(identityModel, offloadCopy: true);
            using var argument = MIGraphXArgument.Create(options.NativePath, program.GetParameterShapes()["input"], IdentityInput);
            using var map = new MIGraphXParameterMap(options.NativePath);
            map.Add("input", argument);
            using var run = program.RunHostAsync(map, stream);
            var completedByQuery = run.TryComplete();
            if (!completedByQuery) run.Synchronize();
            var output = run.Outputs.Single().ToArray<float>();
            Require(output.SequenceEqual(IdentityInput), "Host async output mismatch.");
            return Detail(("completedByPassiveQuery", completedByQuery), ("referenceMatched", true));
        });
        await Run("m6-same-stream-multiple", 3, _ =>
        {
            using var hip = new HipRuntime(options.HipPath);
            using var stream = hip.CreateStream();
            using var program = ParseCompile(identityModel, offloadCopy: true);
            using var firstArgument = MIGraphXArgument.Create(options.NativePath, program.GetParameterShapes()["input"], IdentityInput);
            var secondInput = new[] { 8f, 6f, 7f, 5f };
            using var secondArgument = MIGraphXArgument.Create(options.NativePath, program.GetParameterShapes()["input"], secondInput);
            using var firstMap = new MIGraphXParameterMap(options.NativePath);
            using var secondMap = new MIGraphXParameterMap(options.NativePath);
            firstMap.Add("input", firstArgument);
            secondMap.Add("input", secondArgument);
            using var first = program.RunHostAsync(firstMap, stream);
            using var second = program.RunHostAsync(secondMap, stream);
            first.Synchronize();
            Require(second.TryComplete(), "The second run was not ready after same-stream synchronization.");
            Require(first.Outputs.Single().ToArray<float>().SequenceEqual(IdentityInput), "First same-stream output mismatch.");
            Require(second.Outputs.Single().ToArray<float>().SequenceEqual(secondInput), "Second same-stream output mismatch.");
            return Detail(("runs", 2), ("independentOutputs", true));
        });
        await Run("m6-distinct-stream-isolation", 3, _ =>
        {
            using var hip = new HipRuntime(options.HipPath);
            using var firstStream = hip.CreateStream();
            using var secondStream = hip.CreateStream();
            using var program = ParseCompile(identityModel, offloadCopy: true);
            using var argument = MIGraphXArgument.Create(options.NativePath, program.GetParameterShapes()["input"], IdentityInput);
            using var firstMap = new MIGraphXParameterMap(options.NativePath);
            using var secondMap = new MIGraphXParameterMap(options.NativePath);
            firstMap.Add("input", argument);
            secondMap.Add("input", argument);
            using var first = program.RunHostAsync(firstMap, firstStream);
            using var second = program.RunHostAsync(secondMap, secondStream);
            first.Synchronize();
            var secondCompletedByQuery = second.TryComplete();
            if (!secondCompletedByQuery) second.Synchronize();
            Require(first.Outputs.Single().ToArray<float>().SequenceEqual(IdentityInput), "First distinct-stream output mismatch.");
            Require(second.Outputs.Single().ToArray<float>().SequenceEqual(IdentityInput), "Second distinct-stream output mismatch.");
            return Detail(("streams", 2), ("secondCompletedByPassiveQuery", secondCompletedByQuery), ("completionIsolation", true));
        });
        await Run("m6-early-dispose-leases", 3, _ =>
        {
            using var hip = new HipRuntime(options.HipPath);
            using var stream = hip.CreateStream();
            var program = ParseCompile(identityModel, offloadCopy: true);
            var argument = MIGraphXArgument.Create(options.NativePath, program.GetParameterShapes()["input"], IdentityInput);
            var map = new MIGraphXParameterMap(options.NativePath);
            map.Add("input", argument);
            using var run = program.RunHostAsync(map, stream);
            program.Dispose();
            map.Dispose();
            argument.Dispose();
            run.Synchronize();
            Require(run.Outputs.Single().ToArray<float>().SequenceEqual(IdentityInput), "Early-dispose host output mismatch.");
            return Detail(("callerOwnersDisposedBeforeCompletion", 3), ("referenceMatched", true));
        });
        await Run("m6-device-input-reference", 3, _ =>
        {
            using var hip = new HipRuntime(options.HipPath);
            using var stream = hip.CreateStream();
            using var program = ParseCompile(identityModel, offloadCopy: false);
            var shape = program.GetParameterShapes()["input"];
            var memory = hip.Allocate((ulong)shape.ByteCount);
            memory.CopyFrom(FloatBytes(IdentityInput));
            using var run = program.RunDeviceAsync([new MIGraphXHipDeviceInput("input", shape, memory)], stream);
            memory.Dispose();
            run.Synchronize();
            Require(run.Outputs.Single().ToArray<float>().SequenceEqual(IdentityInput), "Device-input output mismatch.");
            return Detail(("offloadCopy", false), ("h2d", "explicit-sync-before-enqueue"), ("d2h", "explicit-after-stream-completion"), ("referenceMatched", true));
        });
        await Run("m6-device-input-validation", () =>
        {
            using var hip = new HipRuntime(options.HipPath);
            using var otherHip = new HipRuntime(options.HipPath);
            using var stream = hip.CreateStream();
            using var program = ParseCompile(identityModel, offloadCopy: false);
            var shape = program.GetParameterShapes()["input"];
            using var validMemory = hip.Allocate((ulong)shape.ByteCount);
            using var smallMemory = hip.Allocate(4);
            using var otherMemory = otherHip.Allocate((ulong)shape.ByteCount);
            Expect<ArgumentException>(() => program.RunDeviceAsync([new MIGraphXHipDeviceInput("wrong", shape, validMemory)], stream), "device input name");
            Expect<ArgumentException>(() => program.RunDeviceAsync([new MIGraphXHipDeviceInput("input", new MIGraphXShape(MIGraphXShapeDataType.Float32, [2, 2]), validMemory)], stream), "device input shape");
            Expect<ArgumentException>(() => program.RunDeviceAsync([new MIGraphXHipDeviceInput("input", shape, smallMemory)], stream), "device input capacity");
            Expect<ArgumentException>(() => program.RunDeviceAsync([new MIGraphXHipDeviceInput("input", shape, otherMemory)], stream), "device runtime client");
            using var hostProgram = ParseCompile(identityModel, offloadCopy: true);
            Expect<InvalidOperationException>(() => hostProgram.RunDeviceAsync([new MIGraphXHipDeviceInput("input", shape, validMemory)], stream), "device offloadCopy mode");
            using var argument = MIGraphXArgument.Create(options.NativePath, shape, IdentityInput);
            using var map = new MIGraphXParameterMap(options.NativePath);
            map.Add("input", argument);
            Expect<InvalidOperationException>(() => program.RunHostAsync(map, stream), "host offloadCopy mode");
            return Detail(("managedRejections", 6), ("nativeEnqueues", 0));
        });
        await Run("m11-registry-after", () =>
        {
            var names = MIGraphXOnnxWorkflow.GetRegisteredOperators(options.NativePath);
            var hash = Program.HashBytes(JsonSerializer.SerializeToUtf8Bytes(names));
            var before = report.Cases.Single(value => value.Id == "m11-registry-before");
            Require(Convert.ToInt32(before.Detail!["count"]) == names.Count, "Registry count drifted during the functional probe.");
            Require(string.Equals(Convert.ToString(before.Detail["orderedJsonSha256"]), hash, StringComparison.Ordinal), "Registry hash drifted during the functional probe.");
            return Detail(("count", names.Count), ("orderedJsonSha256", hash), ("drift", false));
        });
        Require(Directory.GetFiles(cacheRoot, "*.tmp").Length == 0, "Cache temporary files remain after the functional phase.");
    }

    internal async Task RunCacheRestartAsync()
    {
        await Run("m5-cache-fresh-process-hit", () =>
        {
            Require(Directory.Exists(cacheRoot), "The functional phase cache root is missing.");
            using var fileOptions = new MIGraphXFileOptions(options.NativePath);
            var cache = new MIGraphXModelCache(cacheRoot);
            var metadata = CacheMetadata(identityModel, "offloadCopy=true");
            using var hit = cache.GetOrBuild(metadata, fileOptions, () => throw new InvalidOperationException("Fresh-process cache hit unexpectedly invoked builder."));
            Require(hit.Kind == MIGraphXCacheLookupKind.Hit, "Fresh-process cache lookup did not hit.");
            Compile(hit.Program, offloadCopy: true);
            Require(RunCompiledIdentity(hit.Program, IdentityInput).SequenceEqual(IdentityInput), "Fresh-process cache output mismatch.");
            Require(Directory.GetFiles(cacheRoot, "*.tmp").Length == 0, "Cache temporary files remain after restart validation.");
            return Detail(("kind", hit.Kind.ToString()), ("key", hit.Key), ("sourceSha", options.SourceSha), ("nativeSha256", Program.HashFile(options.NativePath)), ("referenceMatched", true));
        });
    }

    private async Task Run(string id, Func<object?> body)
        => await Run(id, 1, _ => Task.Run(body));

    private async Task Run(string id, Func<Task<object?>> body)
        => await Run(id, 1, _ => body());

    private async Task Run(string id, int iterations, Func<int, object?> body)
        => await Run(id, iterations, iteration => Task.Run(() => body(iteration)));

    private async Task Run(string id, int iterations, Func<int, Task<object?>> body)
    {
        var stopwatch = Stopwatch.StartNew();
        var iterationDurations = new List<long>(iterations);
        try
        {
            object? detail = null;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var iterationStopwatch = Stopwatch.StartNew();
                detail = await body(iteration).WaitAsync(CaseTimeout);
                iterationStopwatch.Stop();
                iterationDurations.Add(iterationStopwatch.ElapsedMilliseconds);
            }
            stopwatch.Stop();
            var resultDetail = ToDictionary(detail) ?? [];
            resultDetail["iterations"] = iterations;
            resultDetail["iterationDurationMilliseconds"] = iterationDurations;
            report.Cases.Add(new CaseResult(id, "passed", stopwatch.ElapsedMilliseconds, resultDetail, null, null));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            report.Cases.Add(new CaseResult(id, "failed", stopwatch.ElapsedMilliseconds, null, exception.GetType().FullName, exception.Message));
            if (exception is TimeoutException) throw;
        }
    }

    private IdentityResult RunIdentity(string? filePath, byte[]? model, float[] input)
    {
        using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
        using var program = filePath is not null
            ? MIGraphXProgram.ParseOnnxFile(filePath, onnxOptions)
            : MIGraphXProgram.ParseOnnxBuffer(model!, onnxOptions);
        var parameter = program.GetParameterShapes().Single();
        Require(parameter.Value.Lengths.SequenceEqual(new long[] { 1, 4 }), "Identity input shape mismatch.");
        Compile(program, offloadCopy: true);
        using var argument = MIGraphXArgument.Create(options.NativePath, parameter.Value, input);
        using var map = new MIGraphXParameterMap(options.NativePath);
        map.Add(parameter.Key, argument);
        using var outputs = program.Run(map);
        Require(outputs.Count == 1, "Identity output count mismatch.");
        return new IdentityResult(parameter.Key, parameter.Value.Lengths.ToArray(), outputs.Single().ToArray<float>());
    }

    private float[] RunStaticOverride(long[] dimensions, float[] input)
    {
        using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
        onnxOptions.SetInputParameterShape("input", dimensions);
        using var program = MIGraphXProgram.ParseOnnxBuffer(dynamicIdentityModel, onnxOptions);
        Require(program.GetParameterShapes()["input"].Lengths.SequenceEqual(dimensions), "Static override snapshot mismatch.");
        Compile(program, offloadCopy: true);
        return RunCompiledIdentity(program, input);
    }

    private float[] RunConcrete(MIGraphXProgram program, string name, long[] dimensions, float[] input)
    {
        using var argument = MIGraphXArgument.Create(options.NativePath, new MIGraphXShape(MIGraphXShapeDataType.Float32, dimensions), input);
        using var map = new MIGraphXParameterMap(options.NativePath);
        map.Add(name, argument);
        using var outputs = program.Run(map);
        return outputs.Single().ToArray<float>();
    }

    private float[] RunCompiledIdentity(MIGraphXProgram program, float[] input)
    {
        var parameter = program.GetParameterShapes().Single();
        return RunConcrete(program, parameter.Key, parameter.Value.Lengths.ToArray(), input);
    }

    private MIGraphXProgram ParseCompile(byte[] model, bool offloadCopy)
    {
        using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
        var program = MIGraphXProgram.ParseOnnxBuffer(model, onnxOptions);
        try
        {
            Compile(program, offloadCopy);
            return program;
        }
        catch
        {
            program.Dispose();
            throw;
        }
    }

    private void Compile(MIGraphXProgram program, bool offloadCopy)
    {
        using var target = new MIGraphXTarget(options.NativePath);
        using var compileOptions = new MIGraphXCompileOptions(options.NativePath, offloadCopy);
        program.Compile(target, compileOptions);
    }

    private MIGraphXProgram BuildIdentity()
    {
        using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
        return MIGraphXProgram.ParseOnnxBuffer(identityModel, onnxOptions);
    }

    private MIGraphXProgram BuildMultiOutput()
    {
        using var onnxOptions = new MIGraphXOnnxOptions(options.NativePath);
        return MIGraphXProgram.ParseOnnxBuffer(multiOutputModel, onnxOptions);
    }

    private MIGraphXCacheMetadata CacheMetadata(byte[] model, string compileOptions)
        => new(
            MIGraphXCacheMetadata.ComputeModelSha256(model),
            "gpu",
            compileOptions,
            "msgpack",
            MIGraphXCacheMetadata.ComputeNativeFingerprint(options.NativePath),
            headerSha256: "a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2",
            apiIdentity: "AMDMIGraphX/rocm-7.2.1",
            managedIdentity: $"JYPPX.ROCm.MIGraphX.CSharp.API/{options.ExpectedVersion}+{options.SourceSha}");

    private static Dictionary<string, object?> CacheDetail(MIGraphXCacheMetadata metadata, MIGraphXCacheLookupKind cold, MIGraphXCacheLookupKind warm)
        => Detail(("key", metadata.ComputeKey()), ("cold", cold.ToString()), ("warm", warm.ToString()));

    private string Fixture(string fileName, string expectedHash)
    {
        var path = Path.GetFullPath(Path.Combine(options.FixtureDirectory, fileName));
        Require(File.Exists(path), $"Fixture is missing: {fileName}.");
        Require(string.Equals(Program.HashFile(path), expectedHash, StringComparison.Ordinal), $"Fixture hash mismatch: {fileName}.");
        return path;
    }

    private (string Sha256, string[] NativeEntries) CaptureProcessMap()
    {
        if (!OperatingSystem.IsLinux()) return ("not-applicable", []);
        var source = "/proc/self/maps";
        var destination = Path.Combine(options.RecordDirectory, "raw", "process-map.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var lines = File.ReadAllLines(source);
        File.WriteAllLines(destination, lines, new UTF8Encoding(false));
        var nativeEntries = lines
            .Where(line => line.Contains("migraphx", StringComparison.OrdinalIgnoreCase) || line.Contains("amdhip", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty)
            .Where(line => line.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Require(nativeEntries.Any(line => string.Equals(Path.GetFullPath(line), options.NativePath, StringComparison.Ordinal)), "The verified native path is absent from the process map.");
        return (Program.HashFile(destination), nativeEntries);
    }

    private static byte[] FloatBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Expect<T>(Action action, string boundary) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name} at {boundary}.");
    }

    private static Dictionary<string, object?> Detail(params (string Key, object? Value)[] values)
        => values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    private static Dictionary<string, object?>? ToDictionary(object? value)
        => value as Dictionary<string, object?> ?? (value is null ? null : Detail(("value", value)));

    private sealed record IdentityResult(string InputName, long[] Shape, float[] Output);
}

internal sealed record ProbeOptions(
    string NativePath,
    string HipPath,
    string FixtureDirectory,
    string RecordDirectory,
    string OutputPath,
    string Phase,
    string SourceSha,
    string ExpectedVersion)
{
    internal static ProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Arguments must be --name value pairs.", nameof(args));
            values.Add(args[index], args[index + 1]);
        }
        string Required(string name) => values.TryGetValue(name, out var value) && value.Length != 0
            ? value
            : throw new ArgumentException($"Missing required argument {name}.", nameof(args));
        string ExistingFile(string name)
        {
            var path = Path.GetFullPath(Required(name));
            if (!Path.IsPathRooted(Required(name)) || !File.Exists(path)) throw new ArgumentException($"{name} must be an existing absolute file.", nameof(args));
            return path;
        }
        string ExistingDirectory(string name)
        {
            var path = Path.GetFullPath(Required(name));
            if (!Path.IsPathRooted(Required(name)) || !Directory.Exists(path)) throw new ArgumentException($"{name} must be an existing absolute directory.", nameof(args));
            return path;
        }
        var sourceSha = Required("--source-sha");
        if (sourceSha.Length != 40 || sourceSha.Any(character => !Uri.IsHexDigit(character))) throw new ArgumentException("--source-sha must be a 40-character hexadecimal commit.", nameof(args));
        var record = ExistingDirectory("--record");
        var output = Path.GetFullPath(Required("--output"));
        var recordPrefix = record.EndsWith(Path.DirectorySeparatorChar) ? record : record + Path.DirectorySeparatorChar;
        if (!output.StartsWith(recordPrefix, StringComparison.Ordinal)) throw new ArgumentException("--output must be inside --record.", nameof(args));
        return new ProbeOptions(
            ExistingFile("--native"),
            ExistingFile("--hip"),
            ExistingDirectory("--fixtures"),
            record,
            output,
            Required("--phase"),
            sourceSha.ToLowerInvariant(),
            Required("--expected-version"));
    }
}

internal sealed class ProbeReport
{
    internal ProbeReport(string phase, string sourceSha, ManagedIdentity? managedIdentity)
    {
        Phase = phase;
        SourceSha = sourceSha;
        ManagedIdentity = managedIdentity;
        StartedUtc = DateTimeOffset.UtcNow;
    }

    public string SchemaVersion => "1.0.0";
    public string State { get; private set; } = "running";
    public string Evidence => EvidenceValue;
    public string Phase { get; }
    public string SourceSha { get; }
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset? CompletedUtc { get; private set; }
    public ManagedIdentity? ManagedIdentity { get; }
    public List<CaseResult> Cases { get; } = [];
    public string? Exception { get; private set; }
    public string? Message { get; private set; }
    private const string EvidenceValue = "runtime-candidate-executed-review-required";

    internal void Complete()
    {
        CompletedUtc = DateTimeOffset.UtcNow;
        State = Cases.Count != 0 && Cases.All(value => value.State == "passed") ? "executed" : "failed";
    }

    internal void Fail(Exception exception)
    {
        CompletedUtc = DateTimeOffset.UtcNow;
        State = "failed";
        Exception = exception.GetType().FullName;
        Message = exception.Message;
    }
}

internal sealed record AssemblyIdentity(string Name, string AssemblyVersion, string FileName, string Sha256);
internal sealed record ManagedIdentity(AssemblyIdentity Core, AssemblyIdentity Adapter, AssemblyIdentity HipSharp, string PackageVersion, string CoreInformationalVersion);
internal sealed record CaseResult(string Id, string State, long DurationMilliseconds, Dictionary<string, object?>? Detail, string? Exception, string? Message);
