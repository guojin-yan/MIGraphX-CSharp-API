using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using JYPPX.ROCm.MIGraphXSharp;

namespace M12RuntimeProbe;

internal static class Program
{
    private const string Evidence = "runtime-candidate-executed-review-required";

    public static int Main(string[] args)
    {
        ProbeOptions? options = null;
        var report = new ProbeReport();
        try
        {
            options = ProbeOptions.Parse(args);
            report.SourceSha = options.SourceSha;
            report.PackageVersion = MIGraphXBuildInfo.PackageVersion;
            VerifyPackageIdentity(options);
            var runner = new ProbeRunner(options, report);
            runner.Run();
            report.State = report.Cases.All(item => item.State == "passed") ? "executed" : "failed";
        }
        catch (Exception exception)
        {
            report.State = "failed";
            report.Exception = exception.GetType().FullName;
            report.Message = exception.Message;
        }

        report.CompletedUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        Directory.CreateDirectory(options?.RecordDirectory ?? Directory.GetCurrentDirectory());
        File.WriteAllText(options?.OutputPath ?? "m12-runtime.json", json + Environment.NewLine);
        Console.WriteLine(json);
        return report.State == "executed" ? 0 : 1;
    }

    private static void VerifyPackageIdentity(ProbeOptions options)
    {
        if (!string.Equals(MIGraphXBuildInfo.PackageVersion, options.ExpectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Core package version is {MIGraphXBuildInfo.PackageVersion}, expected {options.ExpectedVersion}.");
        var assembly = typeof(MIGraphXBuildInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        if (!informational.Contains(options.SourceSha, StringComparison.Ordinal))
            throw new InvalidOperationException("The core package informational version is not bound to the requested source SHA.");
    }

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed class ProbeRunner
{
    private const string ExpectedIdentitySha = "0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9";
    private const string ExpectedTensorFlowFixtureSha = "de8be9fda62bbbffb72ce46ac91426b336be60f882e227b6e71e1407c584740e";
    private const string ExpectedCalibrationFixtureSha = "15f8698707b49e1c92021d833bc0b79c1455f777241e80a7e500619309eda1af";
    private readonly ProbeOptions options;
    private readonly ProbeReport report;

    internal ProbeRunner(ProbeOptions options, ProbeReport report)
    {
        this.options = options;
        this.report = report;
        if (!File.Exists(options.NativePath)) throw new FileNotFoundException("Native library is missing.", options.NativePath);
        if (!File.Exists(options.IdentityFixture)) throw new FileNotFoundException("Identity fixture is missing.", options.IdentityFixture);
        if (!File.Exists(options.TensorFlowFixture)) throw new FileNotFoundException("TensorFlow fixture is missing.", options.TensorFlowFixture);
        if (!File.Exists(options.CalibrationMap)) throw new FileNotFoundException("Calibration map fixture is missing.", options.CalibrationMap);
        VerifyFixtureIdentity(options.IdentityFixture, ExpectedIdentitySha, "Identity fixture");
        VerifyFixtureIdentity(options.TensorFlowFixture, ExpectedTensorFlowFixtureSha, "TensorFlow fixture");
        VerifyFixtureIdentity(options.CalibrationMap, ExpectedCalibrationFixtureSha, "Calibration map fixture");
        VerifyTensorFlowFixture(options.TensorFlowFixture);
        VerifyCalibrationMap(options.CalibrationMap);
        report.FixtureHashes["identityFixtureSha256"] = Program.HashFile(options.IdentityFixture);
        report.FixtureHashes["tensorflowFixtureSha256"] = Program.HashFile(options.TensorFlowFixture);
        report.FixtureHashes["calibrationFixtureSha256"] = Program.HashFile(options.CalibrationMap);
        Directory.CreateDirectory(options.RecordDirectory);
        Directory.CreateDirectory(Path.Combine(options.RecordDirectory, "raw"));
    }

    private static void VerifyFixtureIdentity(string path, string expectedSha, string label)
    {
        if (!string.Equals(Program.HashFile(path), expectedSha, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label} SHA-256 mismatch.");
    }

    private static void VerifyTensorFlowFixture(string path)
    {
        if (new FileInfo(path).Length != 96)
            throw new InvalidOperationException("TensorFlow fixture length is not the reviewed 96-byte GraphDef fixture.");
    }

    private static void VerifyCalibrationMap(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object, "Calibration map root must be an object.");
        Require(root.GetProperty("schemaVersion").GetString() == "1.0.0", "Calibration map schema version is not 1.0.0.");
        Require(root.GetProperty("format").GetString() == "migraphx-calibration-map", "Calibration map format is not MIGraphX calibration-map.");
        Require(root.GetProperty("modelId").GetString() == "m12-identity-float32-1x4", "Calibration map model identity drifted.");
        Require(root.GetProperty("generatedBy").GetString() == "MIGraphX-CSharp-API/eng/generate-m12-fixtures.ps1", "Calibration map generator identity drifted.");
        Require(root.GetProperty("license").GetString() == "Apache-2.0 project-generated fixture", "Calibration map license identity drifted.");
        foreach (var sectionName in new[] { "inputs", "outputs" })
        {
            var entries = root.GetProperty(sectionName);
            Require(entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0, $"Calibration map {sectionName} must contain at least one entry.");
            foreach (var entry in entries.EnumerateArray())
            {
                Require(entry.ValueKind == JsonValueKind.Object, $"Calibration map {sectionName} entry must be an object.");
                Require(entry.GetProperty("dataType").GetString() == "float32", $"Calibration map {sectionName} entry data type drifted.");
                Require(entry.GetProperty("scale").GetDouble() > 0, $"Calibration map {sectionName} entry scale must be positive.");
                Require(entry.GetProperty("zeroPoint").ValueKind == JsonValueKind.Number && entry.GetProperty("zeroPoint").TryGetInt32(out _), $"Calibration map {sectionName} entry zero point must be an integer.");
            }
        }
    }

    internal void Run()
    {
        var cases = new (string Id, Action Action)[]
        {
            ("m12-shape-argument-factories", RunShapeAndArgumentFactories),
            ("m12-argument-persistence-clone", RunArgumentPersistence),
            ("m12-assign-to-clone", RunAssignToClones),
            ("m12-graph-parent-lease", RunGraphParentLease),
            ("m12-graph-editing", RunGraphEditing),
            ("m12-operation-materialized-attributes", RunOperationMaterializedAttributes),
            ("m12-context-lifetime", RunContextLifetime),
            ("m12-negative-variadic-operation", RunNegativeVariadicOperationBoundary),
            ("m12-negative-module-owner", RunNegativeModuleOwnerBoundary),
            ("m12-negative-borrowed-device-clone", RunNegativeBorrowedDeviceCloneBoundary),
            ("m12-tensorflow-parse", RunTensorFlowParse),
            ("m12-quantization-options", RunQuantizationOptions),
            ("m12-custom-op-registration", RunCustomOpRegistration),
            ("m12-concurrent-dispose", RunConcurrentDispose),
        };
        var selected = options.CaseId is null
            ? (options.IncludeDeferred ? cases : cases.Take(9)).ToArray()
            : cases.Where(item => item.Id == options.CaseId).ToArray();
        if (selected.Length == 0) throw new ArgumentException("--case does not name an executable M12 candidate case.");
        foreach (var item in selected) RunCase(item.Id, item.Action);
        foreach (var id in DeferredCases) report.DeferredCaseIds.Add(id);
    }

    private static readonly string[] DeferredCases =
    [
        "m12-tensorflow-parse", "m12-quantization-options", "m12-custom-op-registration",
        "m12-negative-borrowed-device-clone", "m12-negative-variadic-operation",
        "m12-negative-module-owner", "m12-concurrent-dispose", "m12-cross-target-abi"
    ];

    private void RunShapeAndArgumentFactories()
    {
        const string id = "m12-shape-argument-factories";
        WriteStage(id, "scalar", "entered");
        var scalar = MIGraphXShape.CreateScalar(MIGraphXShapeDataType.Float32);
        Require(scalar.Rank == 0 && scalar.Lengths.Count == 0 && scalar.Strides.Count == 0 && scalar.ElementCount == 1 && scalar.ByteCount == sizeof(float), "Managed scalar metadata differs.");
        // MIGraphX 2.15 returns a provider-specific shape snapshot for a native
        // rank-zero empty argument. Keep scalar semantics as a managed contract
        // and use the standard [1,4] fixture shape for native detached arguments.
        WriteStage(id, "scalar-empty", "managed-only");
        WriteStage(id, "strided", "entered");
        var strided = MIGraphXShape.CreateWithStrides(MIGraphXShapeDataType.Float32, new long[] { 2, 2 }, new long[] { 1, 2 });
        Require(strided.Rank == 2 && strided.Lengths.SequenceEqual(new long[] { 2, 2 }) && strided.Strides.SequenceEqual(new long[] { 1, 2 }) && strided.ElementCount == 4 && strided.ByteCount == 4 * sizeof(float), "Explicit stride metadata was not preserved.");
        WriteStage(id, "strided-managed", "validated");
        WriteStage(id, "empty", "entered");
        var emptyShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        using var empty = MIGraphXArgument.CreateEmpty(options.NativePath, emptyShape);
        WriteStage(id, "generated", "entered");
        using var generated = MIGraphXArgument.Generate(options.NativePath, new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 }), 17);
        WriteStage(id, "clone", "entered");
        using var clone = generated.Clone();
        WriteStage(id, "compare", "entered");
        Require(generated.HasSameNativeContent(clone), "Generated argument clone differs from its source.");
        Require(generated.ToArray<float>().SequenceEqual(clone.ToArray<float>()), "Generated argument clone values differ from its source.");
        WriteStage(id, "empty-shape", "entered");
        Require(empty.Shape.HasSameNativeContent(emptyShape), "Empty argument shape snapshot differs.");
    }

    private void RunArgumentPersistence()
    {
        const string id = "m12-argument-persistence-clone";
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        var path = Path.Combine(options.RecordDirectory, "argument.msgpack");
        WriteStage(id, "create", "entered");
        var original = MIGraphXArgument.Create(options.NativePath, shape, new[] { 0.25f, -1f, 2f, 9f });
        WriteStage(id, "save", "entered");
        original.Save(path);
        WriteStage(id, "source-dispose", "entered");
        original.Dispose();
        WriteStage(id, "load", "entered");
        var loaded = MIGraphXArgument.Load(options.NativePath, path);
        WriteStage(id, "clone", "entered");
        using var clone = loaded.Clone();
        WriteStage(id, "compare", "entered");
        Require(loaded.HasSameNativeContent(clone), "Loaded argument clone differs from its source.");
        WriteStage(id, "loaded-dispose", "entered");
        loaded.Dispose();
        WriteStage(id, "readback", "entered");
        Require(clone.ToArray<float>().SequenceEqual(new[] { 0.25f, -1f, 2f, 9f }), "Argument persistence values differ.");
        WriteStage(id, "collection-clone", "entered");
        using var program = ParseIdentity(new MIGraphXOnnxOptions(options.NativePath));
        var parameters = CreateFloatParameters(program.GetParameterShapes());
        using var parameterClone = parameters.Clone();
        parameters.Dispose();
        using var target = new MIGraphXTarget(options.NativePath);
        using var compileOptions = new MIGraphXCompileOptions(options.NativePath);
        program.Compile(target, compileOptions);
        var outputs = program.Run(parameterClone);
        using var outputClone = outputs.Clone();
        outputs.Dispose();
        Require(ReadFloatOutputs(outputClone, "Cloned output collection").SequenceEqual(new[] { 0.25f, -1f, 2f, 9f }), "Collection clone values differ after source disposal.");
        WriteStage(id, "hash", "entered");
        report.Artifacts["argumentSha256"] = Program.HashFile(path);
    }

    private void RunAssignToClones()
    {
        var target = new MIGraphXTarget(options.NativePath);
        using var targetClone = target.Clone();
        target.Dispose();
        var compile = new MIGraphXCompileOptions(options.NativePath, true, true, false);
        using var compileClone = compile.Clone();
        compile.Dispose();
        var file = new MIGraphXFileOptions(options.NativePath);
        using var fileClone = file.Clone();
        file.Dispose();
        var onnx = new MIGraphXOnnxOptions(options.NativePath);
        onnx.SetInputParameterShape("input", new long[] { 1, 4 });
        using var onnxClone = onnx.Clone();
        onnx.Dispose();
        Require(targetClone.Name == "gpu" && compileClone.OffloadCopy && compileClone.FastMath && !compileClone.ExhaustiveTune && fileClone.FileFormat == "msgpack", "Assign-to clone metadata differs.");
        var program = ParseIdentity(onnxClone);
        var parameterShape = program.GetParameterShapes()["input"];
        Require(parameterShape.Lengths.SequenceEqual(new long[] { 1, 4 }), "Cloned ONNX options did not preserve the input override.");
        using var programClone = program.Clone();
        Require(program.HasSameNativeContent(programClone), "Program assign-to clone differs from its source.");
        program.Dispose();
        programClone.Compile(targetClone, compileClone);
        Require(programClone.IsCompiled, "Program clone did not remain usable after source disposal.");
        var path = Path.Combine(options.RecordDirectory, "assign-to-clone.mxr");
        programClone.Save(path, fileClone);
        Require(File.Exists(path) && new FileInfo(path).Length > 0, "File-options clone did not persist the program after source disposal.");
        report.Artifacts["assignToCloneSha256"] = Program.HashFile(path);
    }

    private void RunGraphParentLease()
    {
        const string id = "m12-graph-parent-lease";
        WriteStage(id, "parse", "entered");
        var program = ParseIdentity(new MIGraphXOnnxOptions(options.NativePath));
        WriteStage(id, "target", "entered");
        using (var target = new MIGraphXTarget(options.NativePath))
        using (var compileOptions = new MIGraphXCompileOptions(options.NativePath))
        {
            WriteStage(id, "compile", "entered");
            program.Compile(target, compileOptions);
        }
        WriteStage(id, "child-module", "entered");
        using var child = program.CreateModule("m12-child");
        WriteStage(id, "main-module", "entered");
        using var main = program.GetMainModule();
        WriteStage(id, "module-collections", "entered");
        using var modules = new MIGraphXModules(options.NativePath, new[] { main, child });
        using var moduleClone = modules.Clone();
        WriteStage(id, "program-dispose", "entered");
        program.Dispose();
        WriteStage(id, "main-print", "entered");
        main.Print();
        WriteStage(id, "child-print", "entered");
        child.Print();
        Require(modules.Count == 2 && moduleClone.Count == 2, "Module collections did not retain both program-owned views.");
        WriteStage(id, "teardown", "entered");
        main.Dispose();
        child.Dispose();
    }

    private void RunGraphEditing()
    {
        using var program = new MIGraphXProgram(options.NativePath);
        using var module = program.GetMainModule();
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        using var parameter = module.AddParameter("input", shape);
        using var literalValue = MIGraphXArgument.Create(options.NativePath, shape, new[] { 0f, 0f, 0f, 0f });
        using var literal = module.AddLiteral(literalValue);
        using var allocation = module.AddAllocation(shape);
        using var addArguments = new MIGraphXInstructions(options.NativePath, new[] { parameter, literal });
        using var add = MIGraphXOperation.Create(options.NativePath, "add");
        using var sum = module.AddInstruction(add, addArguments);
        using var returnArguments = new MIGraphXInstructions(options.NativePath, new[] { sum });
        using var returned = module.AddReturn(returnArguments);
        module.Print();
        WriteStage("m12-graph-editing", "compile", "entered");
        using var target = new MIGraphXTarget(options.NativePath);
        using var compileOptions = new MIGraphXCompileOptions(options.NativePath);
        program.Compile(target, compileOptions);
        using var parameters = CreateFloatParameters(program.GetParameterShapes());
        WriteStage("m12-graph-editing", "run", "entered");
        using var outputs = program.Run(parameters);
        Require(ReadFloatOutputs(outputs, "Edited graph output").SequenceEqual(new[] { 0.25f, -1f, 2f, 9f }), "Edited graph output does not match the add-zero reference.");
    }

    private void RunContextLifetime()
    {
        const string id = "m12-context-lifetime";
        WriteStage(id, "parse", "entered");
        var program = ParseIdentity(new MIGraphXOnnxOptions(options.NativePath));
        WriteStage(id, "target", "entered");
        using (var target = new MIGraphXTarget(options.NativePath))
        using (var compileOptions = new MIGraphXCompileOptions(options.NativePath))
        {
            WriteStage(id, "compile", "entered");
            program.Compile(target, compileOptions);
        }
        WriteStage(id, "context", "entered");
        using var context = program.GetExperimentalContext();
        WriteStage(id, "program-dispose", "entered");
        program.Dispose();
        WriteStage(id, "context-finish", "entered");
        context.Finish();
        WriteStage(id, "context-queue", "entered");
        Require(context.Queue != IntPtr.Zero, "Context queue was null.");
        WriteStage(id, "teardown", "entered");
    }

    private void RunOperationMaterializedAttributes()
    {
        const string id = "m12-operation-materialized-attributes";
        var cases = new (string Name, MIGraphXOperationAttributes Attributes)[]
        {
            ("reshape", MIGraphXOperationAttributes.ForReshape(1, 4)),
            ("transpose", MIGraphXOperationAttributes.ForTranspose(1, 0)),
            ("slice", MIGraphXOperationAttributes.ForSlice(new long[] { 0 }, new long[] { 0 }, new long[] { 1 })),
            ("multibroadcast", MIGraphXOperationAttributes.ForMultibroadcast(1, 4)),
            ("topk", MIGraphXOperationAttributes.ForTopK(1, 1, true)),
        };
        var observations = new List<string>(cases.Length);
        foreach (var item in cases)
        {
            WriteStage(id, item.Name, "entered");
            using var operation = MIGraphXOperation.Create(options.NativePath, item.Name, item.Attributes);
            using var clone = operation.Clone();
            Require(operation.Name == item.Name && clone.Name == item.Name, $"Operation name mismatch for {item.Name}.");
            observations.Add($"{item.Name}|{item.Attributes.Build()}|{clone.Name}");
        }
        var artifact = Path.Combine(options.RecordDirectory, "operation-attributes.txt");
        File.WriteAllLines(artifact, observations);
        report.Artifacts["operationAttributesSha256"] = Program.HashFile(artifact);
        WriteStage(id, "teardown", "entered");
    }

    private void RunNegativeVariadicOperationBoundary()
    {
        const string id = "m12-negative-variadic-operation";
        WriteStage(id, "reflection", "entered");
        var attributeMethods = typeof(MIGraphXOperationAttributes)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();
        Require(attributeMethods.Length > 0, "Operation attribute surface is empty.");
        Require(!attributeMethods.Any(method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(object)
            || parameter.ParameterType == typeof(object[])
            || parameter.ParameterType == typeof(IntPtr)
            || parameter.ParameterType.IsPointer
            || (parameter.GetCustomAttribute<ParamArrayAttribute>() is not null
                && parameter.ParameterType.GetElementType() == typeof(object)))),
            "Operation attributes expose an arbitrary variadic ABI carrier.");

        var createMethods = typeof(MIGraphXOperation)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(MIGraphXOperation.Create))
            .ToArray();
        Require(createMethods.Length == 2, "Operation create overload count drifted.");
        Require(createMethods.Any(method => method.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual(new[] { typeof(string), typeof(string) })),
            "The no-attribute operation factory is missing.");
        Require(createMethods.Any(method => method.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual(new[] { typeof(string), typeof(string), typeof(MIGraphXOperationAttributes) })),
            "The typed-attribute operation factory is missing.");
        Require(!createMethods.Any(method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(object)
            || parameter.ParameterType == typeof(object[])
            || parameter.ParameterType == typeof(IntPtr)
            || parameter.ParameterType.IsPointer
            || parameter.GetCustomAttribute<ParamArrayAttribute>() is not null)),
            "Operation creation exposes an arbitrary variadic ABI carrier.");
        var booleanArray = new MIGraphXOperationAttributes().SetBooleanArray("flags", new[] { true, false });
        Require(booleanArray.Build() == "{flags: [true, false]}", "Boolean-array attributes were not materialized deterministically.");
        var typedScalars = new MIGraphXOperationAttributes().SetInt32("i32", -2).SetUInt32("u32", 3u).SetInt64("i64", -4L).SetUInt64("u64", 5UL).SetSingle("single", 0.5f).SetDouble("double", 1.25).SetBoolean("enabled", true).SetString("mode", "nearest").SetNull("optional");
        Require(typedScalars.Build() == "{i32: -2, u32: 3, i64: -4, u64: 5, single: 0.5, double: 1.25, enabled: true, mode: \"nearest\", optional: null}", "Typed scalar attributes were not materialized deterministically.");
        var typedArrays = new MIGraphXOperationAttributes().SetInt32Array("i32s", new[] { -1, 2 }).SetUInt32Array("u32s", new[] { 3u, 4u }).SetInt64Array("i64s", new[] { -5L, 6L }).SetUInt64Array("u64s", new[] { 7UL, 8UL }).SetSingleArray("singles", new[] { 0.5f, -1f }).SetDoubleArray("doubles", new[] { 1.25, 2.5 }).SetBooleanArray("bools", new[] { true, false }).SetStringArray("labels", new[] { "a", "b" });
        Require(typedArrays.Build() == "{i32s: [-1, 2], u32s: [3, 4], i64s: [-5, 6], u64s: [7, 8], singles: [0.5, -1], doubles: [1.25, 2.5], bools: [true, false], labels: [\"a\", \"b\"]}", "Typed array attributes were not materialized deterministically.");

        AppendNegativeBoundaryObservation("variadic-operation|two-constrained-create-overloads|no-object-pointer-or-params-object|boolean-array-materialized|all-typed-scalars-and-arrays-materialized");
        WriteStage(id, "teardown", "entered");
    }

    private void RunNegativeModuleOwnerBoundary()
    {
        const string id = "m12-negative-module-owner";
        WriteStage(id, "reflection", "entered");
        var moduleType = typeof(MIGraphXModule);
        Require(moduleType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0,
            "MIGraphXModule exposes an independent public owner constructor.");
        Require(!moduleType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Any(method => !method.IsSpecialName),
            "MIGraphXModule exposes an independent public owner factory.");

        var createModuleMethods = typeof(MIGraphXProgram)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(MIGraphXProgram.CreateModule))
            .ToArray();
        Require(createModuleMethods.Length == 1
            && createModuleMethods[0].ReturnType == typeof(MIGraphXModule)
            && createModuleMethods[0].GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { typeof(string) }),
            "Program-bound module factory contract drifted.");

        AppendNegativeBoundaryObservation("module-owner|no-public-module-constructor-or-static-factory|program-bound-create-module-only");
        WriteStage(id, "teardown", "entered");
    }

    private void RunNegativeBorrowedDeviceCloneBoundary()
    {
        const string id = "m12-negative-borrowed-device-clone";
        var assembly = typeof(MIGraphXArgument).Assembly;
        var runtimeType = assembly.GetType("JYPPX.ROCm.MIGraphXSharp.Interop.NativeRuntime", throwOnError: true)!;
        var loadRuntime = runtimeType.GetMethod(
            "Load",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null)
            ?? throw new InvalidOperationException("The internal native-runtime loader is missing.");
        var createExternal = typeof(MIGraphXArgument).GetMethod(
            "CreateExternal",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { runtimeType, typeof(MIGraphXShape), typeof(IntPtr) },
            modifiers: null)
            ?? throw new InvalidOperationException("The internal borrowed-argument factory is missing.");

        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        var expected = Enumerable.Range(0, checked((int)shape.ByteCount)).Select(index => checked((byte)(0xa0 + index))).ToArray();
        var pointer = Marshal.AllocHGlobal(expected.Length);
        try
        {
            Marshal.Copy(expected, 0, pointer, expected.Length);
            WriteStage(id, "external-buffer", "entered");
            var runtime = loadRuntime.Invoke(null, new object[] { options.NativePath })
                ?? throw new InvalidOperationException("The internal native-runtime loader returned null.");
            using var borrowed = createExternal.Invoke(null, new[] { runtime, shape, pointer }) as MIGraphXArgument
                ?? throw new InvalidOperationException("The internal borrowed-argument factory returned null.");

            WriteStage(id, "clone", "entered");
            try
            {
                using var unexpected = borrowed.Clone();
                throw new InvalidOperationException("Borrowed argument clone unexpectedly succeeded.");
            }
            catch (NotSupportedException exception)
            {
                Require(exception.Message == "A borrowed device argument cannot be cloned into an independent host buffer.",
                    "Borrowed argument clone rejection changed.");
            }

            var afterRejection = new byte[expected.Length];
            Marshal.Copy(pointer, afterRejection, 0, afterRejection.Length);
            Require(afterRejection.SequenceEqual(expected), "Borrowed external buffer changed after clone rejection.");
            WriteStage(id, "source-dispose", "entered");
            borrowed.Dispose();
            var afterDispose = new byte[expected.Length];
            Marshal.Copy(pointer, afterDispose, 0, afterDispose.Length);
            Require(afterDispose.SequenceEqual(expected), "Borrowed external buffer changed after source disposal.");
            report.Artifacts["borrowedExternalLeaseSha256"] = Program.HashBytes(afterDispose);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        AppendNegativeBoundaryObservation("borrowed-device-clone|internal-external-buffer-construction|managed-not-supported-rejection|source-lease-unchanged|no-gpu-allocation");
        WriteStage(id, "teardown", "entered");
    }

    private void AppendNegativeBoundaryObservation(string observation)
    {
        var artifact = Path.Combine(options.RecordDirectory, "negative-boundaries.txt");
        File.AppendAllText(artifact, observation + Environment.NewLine);
        report.Artifacts["negativeBoundariesSha256"] = Program.HashFile(artifact);
    }

    private void RunTensorFlowParse()
    {
        const string id = "m12-tensorflow-parse";
        var model = File.ReadAllBytes(options.TensorFlowFixture);
        WriteStage(id, "options", "entered");
        using var fileOptions = new MIGraphXTfOptions(options.NativePath);
        fileOptions.SetInputParameterShape("input", new long[] { 1, 4 });
        fileOptions.SetOutputNames(new[] { "output" });
        using var bufferOptions = fileOptions.Clone();
        WriteStage(id, "file-parse", "entered");
        using var fileProgram = MIGraphXProgram.ParseTfFile(options.TensorFlowFixture, fileOptions);
        WriteStage(id, "buffer-parse", "entered");
        using var bufferProgram = MIGraphXProgram.ParseTfBuffer(model, bufferOptions);
        CompareShapeMaps(fileProgram.GetParameterShapes(), bufferProgram.GetParameterShapes(), "TensorFlow parameter shapes");
        CompareShapeLists(fileProgram.GetOutputShapes(), bufferProgram.GetOutputShapes(), "TensorFlow output shapes");

        WriteStage(id, "compile", "entered");
        using var target = new MIGraphXTarget(options.NativePath);
        using var compileOptions = new MIGraphXCompileOptions(options.NativePath);
        fileProgram.Compile(target, compileOptions);
        bufferProgram.Compile(target, compileOptions);
        using var fileParameters = CreateFloatParameters(fileProgram.GetParameterShapes());
        using var bufferParameters = CreateFloatParameters(bufferProgram.GetParameterShapes());
        WriteStage(id, "run", "entered");
        using var fileOutputs = fileProgram.Run(fileParameters);
        using var bufferOutputs = bufferProgram.Run(bufferParameters);
        CompareOutputs(fileOutputs, bufferOutputs, "TensorFlow file/buffer outputs", 0.0001f);
        report.Artifacts["tensorflowOutputCount"] = fileOutputs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        WriteStage(id, "teardown", "entered");
    }

    private void RunQuantizationOptions()
    {
        const string id = "m12-quantization-options";
        var calibrationInputs = ReadCalibrationInputNames();
        WriteStage(id, "calibration", "entered");
        var int8Output = RunQuantizationVariant("int8", calibrationInputs, (program, target, data) =>
        {
            using var options = new MIGraphXQuantizeInt8Options(this.options.NativePath);
            options.AddCalibrationData(data);
            program.QuantizeInt8(target, options);
        });
        var fp8Output = RunQuantizationVariant("fp8", calibrationInputs, (program, target, data) =>
        {
            using var options = new MIGraphXQuantizeFp8Options(this.options.NativePath);
            options.AddCalibrationData(data);
            program.QuantizeFp8(target, options);
        });
        report.Artifacts["quantizationOutputSha256"] = Program.HashBytes(int8Output.Concat(fp8Output).ToArray());
        WriteStage(id, "teardown", "entered");
    }

    private byte[] RunQuantizationVariant(
        string variant,
        IReadOnlyList<string> calibrationInputs,
        Action<MIGraphXProgram, MIGraphXTarget, MIGraphXParameterMap> quantize)
    {
        WriteStage("m12-quantization-options", variant + "-parse", "entered");
        using var onnx = new MIGraphXOnnxOptions(options.NativePath);
        onnx.SetInputParameterShape("input", new long[] { 1, 4 });
        using var program = MIGraphXProgram.ParseOnnxFile(options.IdentityFixture, onnx);
        using var target = new MIGraphXTarget(options.NativePath);
        using var compileOptions = new MIGraphXCompileOptions(options.NativePath);
        WriteStage("m12-quantization-options", variant + "-parameters", "entered");
        using var parameters = CreateFloatParameters(program.GetParameterShapes(), calibrationInputs);
        WriteStage("m12-quantization-options", variant + "-quantize", "entered");
        quantize(program, target, parameters);
        Require(!program.IsCompiled, $"{variant} quantization did not invalidate the compiled state.");
        WriteStage("m12-quantization-options", variant + "-compile", "entered");
        program.Compile(target, compileOptions);
        WriteStage("m12-quantization-options", variant + "-run", "entered");
        using var outputs = program.Run(parameters);
        var values = ReadFloatOutputs(outputs, $"{variant} quantized output");
        Require(values.All(value => !float.IsNaN(value) && !float.IsInfinity(value)), $"{variant} quantization produced a non-finite value.");
        return values.SelectMany(BitConverter.GetBytes).ToArray();
    }

    private void RunCustomOpRegistration()
    {
        const string id = "m12-custom-op-registration";
        var callbackInvocations = 0;
        var state = new object();
        WriteStage(id, "create", "entered");
        using var operation = new MIGraphXExperimentalCustomOp(options.NativePath, "m12_runtime_custom_op", state);
        operation.SetCompute((_, _, _, _, _, _, _) => { Interlocked.Increment(ref callbackInvocations); return MIGraphXStatus.Success; });
        operation.SetComputeShape((_, _, _, _, _) => { Interlocked.Increment(ref callbackInvocations); return MIGraphXStatus.Success; });
        operation.SetOutputAlias((_, _, _, _, _, _) => { Interlocked.Increment(ref callbackInvocations); return MIGraphXStatus.Success; });
        operation.SetRunsOnOffloadTarget((_, _, _, _) => { Interlocked.Increment(ref callbackInvocations); return MIGraphXStatus.Success; });
        WriteStage(id, "clone", "entered");
        using var clone = operation.Clone();
        Require(ReferenceEquals(operation.State, state) && ReferenceEquals(clone.State, state), "Custom-op managed state was not retained by clone.");
        WriteStage(id, "register", "entered");
        operation.Register();
        clone.Register();
        report.Artifacts["customOpNativeCallbackInvocations"] = callbackInvocations.ToString(System.Globalization.CultureInfo.InvariantCulture);
        report.Artifacts["customOpCallbackExecutionVerified"] = "false";
        WriteStage(id, "teardown", "entered");
    }

    private void RunConcurrentDispose()
    {
        const string id = "m12-concurrent-dispose";
        var races = 0;
        WriteStage(id, "argument", "entered");
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        using (var argument = MIGraphXArgument.Create(options.NativePath, shape, new[] { 0.25f, -1f, 2f, 9f }))
        {
            RunDisposeRace(() => argument.ToArray<float>(), argument.Dispose);
            races++;
        }
        WriteStage(id, "compile-options", "entered");
        using (var compileOptions = new MIGraphXCompileOptions(options.NativePath))
        {
            RunDisposeRace(() => { using var clone = compileOptions.Clone(); }, compileOptions.Dispose);
            races++;
        }
        WriteStage(id, "program", "entered");
        using (var program = new MIGraphXProgram(options.NativePath))
        {
            RunDisposeRace(() => _ = program.IsCompiled, program.Dispose);
            races++;
        }
        WriteStage(id, "custom-op", "entered");
        using (var customOp = new MIGraphXExperimentalCustomOp(options.NativePath, "m12_runtime_concurrent_custom_op"))
        {
            RunDisposeRace(customOp.Register, customOp.Dispose);
            races++;
        }
        WriteStage(id, "quantization-options", "entered");
        using (var names = new MIGraphXQuantizeOpNames(options.NativePath))
        {
            RunDisposeRace(() => names.Add("identity"), names.Dispose);
            races++;
        }
        report.Artifacts["disposeRaceCount"] = races.ToString(System.Globalization.CultureInfo.InvariantCulture);
        WriteStage(id, "teardown", "entered");
    }

    private void RunDisposeRace(Action access, Action dispose)
    {
        var started = new ManualResetEventSlim(false);
        var unexpected = new ConcurrentBag<Exception>();
        try
        {
            var worker = Task.Run(() =>
            {
                started.Set();
                for (var index = 0; index < 256; index++)
                {
                    try { access(); }
                    catch (ObjectDisposedException) { return; }
                    catch (Exception exception) { unexpected.Add(exception); return; }
                }
            });
            Require(started.Wait(TimeSpan.FromSeconds(5)), "Concurrent dispose worker did not start.");
            dispose();
            Require(worker.Wait(TimeSpan.FromSeconds(30)), "Concurrent dispose worker did not complete.");
            if (!unexpected.IsEmpty) throw new InvalidOperationException("Concurrent access produced an unexpected exception.", unexpected.First());
            dispose();
        }
        finally
        {
            started.Dispose();
        }
    }

    private IReadOnlyList<string> ReadCalibrationInputNames()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(options.CalibrationMap));
        return document.RootElement.GetProperty("inputs").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString() ?? throw new InvalidOperationException("Calibration input name is null."))
            .ToArray();
    }

    private MIGraphXParameterMap CreateFloatParameters(IReadOnlyDictionary<string, MIGraphXShape> shapes, IReadOnlyList<string>? requiredNames = null)
    {
        var required = requiredNames is null ? null : new HashSet<string>(requiredNames, StringComparer.Ordinal);
        var map = new MIGraphXParameterMap(options.NativePath);
        try
        {
            foreach (var pair in shapes)
            {
                if (required is not null && !required.Contains(pair.Key)) continue;
                if (pair.Value.DataType != MIGraphXShapeDataType.Float32 || pair.Value.IsDynamic || !pair.Value.IsStandard || !pair.Value.IsPacked)
                    throw new NotSupportedException($"M12 candidate parameters require static packed float32 shapes: {pair.Key}.");
                var values = pair.Value.ElementCount == 4
                    ? new[] { 0.25f, -1f, 2f, 9f }
                    : Enumerable.Range(0, checked((int)pair.Value.ElementCount)).Select(index => 0.25f + index).ToArray();
                using var argument = MIGraphXArgument.Create(options.NativePath, pair.Value, values);
                map.Add(pair.Key, argument);
            }
            if (required is not null && !required.SetEquals(map.Names)) throw new InvalidOperationException("Calibration input names do not match the parsed program parameters.");
            return map;
        }
        catch
        {
            map.Dispose();
            throw;
        }
    }

    private static void CompareShapeMaps(IReadOnlyDictionary<string, MIGraphXShape> left, IReadOnlyDictionary<string, MIGraphXShape> right, string label)
    {
        Require(left.Count == right.Count && left.Keys.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(right.Keys.OrderBy(value => value, StringComparer.Ordinal)), $"{label} names differ.");
        foreach (var pair in left) Require(pair.Value.HasSameNativeContent(right[pair.Key]), $"{label} differ for '{pair.Key}'.");
    }

    private static void CompareShapeLists(IReadOnlyList<MIGraphXShape> left, IReadOnlyList<MIGraphXShape> right, string label)
    {
        Require(left.Count == right.Count, $"{label} count differs.");
        for (var index = 0; index < left.Count; index++) Require(left[index].HasSameNativeContent(right[index]), $"{label} differ at index {index}.");
    }

    private static void CompareOutputs(MIGraphXArgumentCollection left, MIGraphXArgumentCollection right, string label, float tolerance)
    {
        Require(left.Count == right.Count, $"{label} count differs.");
        for (var index = 0; index < left.Count; index++)
        {
            var leftValues = left[index].ToArray<float>();
            var rightValues = right[index].ToArray<float>();
            Require(leftValues.Length == rightValues.Length && leftValues.Zip(rightValues, (a, b) => Math.Abs(a - b)).All(delta => delta <= tolerance), $"{label} differ at output {index}.");
        }
    }

    private static float[] ReadFloatOutputs(MIGraphXArgumentCollection outputs, string label)
    {
        Require(outputs.Count > 0, $"{label} is empty.");
        return outputs.SelectMany(output => output.ToArray<float>()).ToArray();
    }

    private MIGraphXProgram ParseIdentity(MIGraphXOnnxOptions onnx)
    {
        try { return MIGraphXProgram.ParseOnnxFile(options.IdentityFixture, onnx); }
        finally { onnx.Dispose(); }
    }

    private void RunCase(string id, Action action)
    {
        var started = DateTimeOffset.UtcNow;
        WriteStage(id, "case", "started");
        try
        {
            action();
            WriteStage(id, "case", "completed");
            report.Cases.Add(new ProbeCase(id, "passed", DateTimeOffset.UtcNow - started, null));
        }
        catch (Exception exception)
        {
            WriteStage(id, "exception", $"{exception.GetType().FullName}: {exception.Message}");
            WriteStage(id, "case", "failed");
            report.Cases.Add(new ProbeCase(id, "failed", DateTimeOffset.UtcNow - started, exception.GetType().FullName));
        }
    }

    private void WriteStage(string caseId, string stage, string state)
    {
        var entry = JsonSerializer.Serialize(new { schemaVersion = "1.0.0", caseId, stage, state, utc = DateTimeOffset.UtcNow });
        File.AppendAllText(Path.Combine(options.RecordDirectory, "raw", "case-stages.jsonl"), entry + Environment.NewLine);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class ProbeOptions
{
    public required string NativePath { get; init; }
    public required string IdentityFixture { get; init; }
    public required string TensorFlowFixture { get; init; }
    public required string CalibrationMap { get; init; }
    public required string RecordDirectory { get; init; }
    public required string OutputPath { get; init; }
    public required string SourceSha { get; init; }
    public required string ExpectedVersion { get; init; }
    public string? CaseId { get; init; }
    public bool IncludeDeferred { get; init; }

    internal static ProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length;)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Arguments must be --name value pairs.");
            if (string.Equals(args[index], "--include-deferred", StringComparison.Ordinal))
            {
                values.Add(args[index], "true");
                index++;
                continue;
            }
            if (index + 1 >= args.Length) throw new ArgumentException("Arguments must be --name value pairs.");
            values.Add(args[index], args[index + 1]);
            index += 2;
        }
        string Required(string name) => values.TryGetValue(name, out var value) && value.Length != 0 ? value : throw new ArgumentException($"Missing required argument {name}.");
        string ExistingFile(string name) { var supplied = Required(name); var path = Path.GetFullPath(supplied); if (!Path.IsPathRooted(supplied) || !File.Exists(path)) throw new ArgumentException($"{name} must be an existing absolute file."); return path; }
        var source = Required("--source-sha");
        if (source.Length != 40 || source.Any(value => !Uri.IsHexDigit(value))) throw new ArgumentException("--source-sha must be a 40-character hexadecimal SHA.");
        var includeDeferred = values.ContainsKey("--include-deferred");
        if (includeDeferred && values.ContainsKey("--case")) throw new ArgumentException("--include-deferred cannot be combined with --case.");
        var record = Path.GetFullPath(Required("--record"));
        Directory.CreateDirectory(record);
        var output = Path.GetFullPath(Required("--output"));
        var prefix = record.EndsWith(Path.DirectorySeparatorChar) ? record : record + Path.DirectorySeparatorChar;
        if (!output.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("--output must be inside --record.");
        values.TryGetValue("--case", out var caseId);
        return new ProbeOptions
        {
            NativePath = ExistingFile("--native"),
            IdentityFixture = ExistingFile("--identity"),
            TensorFlowFixture = ExistingFile("--tensorflow-fixture"),
            CalibrationMap = ExistingFile("--calibration-map"),
            RecordDirectory = record,
            OutputPath = output,
            SourceSha = source.ToLowerInvariant(),
            ExpectedVersion = Required("--expected-version"),
            CaseId = caseId,
            IncludeDeferred = includeDeferred
        };
    }
}

internal sealed class ProbeReport
{
    public string SchemaVersion { get; } = "1.0.0";
    public string Evidence { get; } = "runtime-candidate-executed-review-required";
    public string State { get; set; } = "failed";
    public string SourceSha { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public DateTimeOffset CompletedUtc { get; set; }
    public List<ProbeCase> Cases { get; } = [];
    public List<string> DeferredCaseIds { get; } = [];
    public Dictionary<string, string> Artifacts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> FixtureHashes { get; } = new(StringComparer.Ordinal);
    public string? Exception { get; set; }
    public string? Message { get; set; }
}

internal sealed record ProbeCase(string Id, string State, TimeSpan Duration, string? Exception);
