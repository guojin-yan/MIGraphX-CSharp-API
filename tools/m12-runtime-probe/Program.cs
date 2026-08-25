using System.Reflection;
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
}

internal sealed class ProbeRunner
{
    private const string ExpectedIdentitySha = "0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9";
    private const string ExpectedTensorFlowFixtureSha = "de8be9fda62bbbffb72ce46ac91426b336be60f882e227b6e71e1407c584740e";
    private const string ExpectedCalibrationFixtureSha = "5863a18402ce36040db602b09e878214bb0bf71d623e55284ae8fa35143c8f1f";
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
            ("m12-context-lifetime", RunContextLifetime),
        };
        var selected = options.CaseId is null ? cases : cases.Where(item => item.Id == options.CaseId).ToArray();
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
        WriteStage(id, "strided", "entered");
        var strided = MIGraphXShape.CreateWithStrides(MIGraphXShapeDataType.Float32, new long[] { 2, 2 }, new long[] { 1, 2 });
        Require(strided.Rank == 2 && strided.Strides.SequenceEqual(new long[] { 1, 2 }), "Explicit strides were not preserved.");
        WriteStage(id, "empty", "entered");
        var emptyShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        using var empty = MIGraphXArgument.CreateEmpty(options.NativePath, emptyShape);
        WriteStage(id, "generated", "entered");
        using var generated = MIGraphXArgument.Generate(options.NativePath, new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 }), 17);
        WriteStage(id, "clone", "entered");
        using var clone = generated.Clone();
        WriteStage(id, "compare", "entered");
        Require(generated.HasSameNativeContent(clone), "Generated argument clone differs from its source.");
        WriteStage(id, "empty-shape", "entered");
        Require(empty.Shape.HasSameNativeContent(emptyShape), "Empty argument shape snapshot differs.");
    }

    private void RunArgumentPersistence()
    {
        const string id = "m12-argument-persistence-clone";
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        var path = Path.Combine(options.RecordDirectory, "argument.msgpack");
        WriteStage(id, "create", "entered");
        using var original = MIGraphXArgument.Create(options.NativePath, shape, new[] { 0.25f, -1f, 2f, 9f });
        WriteStage(id, "save", "entered");
        original.Save(path);
        WriteStage(id, "load", "entered");
        using var loaded = MIGraphXArgument.Load(options.NativePath, path);
        WriteStage(id, "clone", "entered");
        using var clone = loaded.Clone();
        WriteStage(id, "compare", "entered");
        Require(loaded.HasSameNativeContent(clone), "Loaded argument clone differs from its source.");
        WriteStage(id, "readback", "entered");
        Require(clone.ToArray<float>().SequenceEqual(new[] { 0.25f, -1f, 2f, 9f }), "Argument persistence values differ.");
        WriteStage(id, "hash", "entered");
        report.Artifacts["argumentSha256"] = Program.HashFile(path);
    }

    private void RunAssignToClones()
    {
        using var target = new MIGraphXTarget(options.NativePath);
        using var targetClone = target.Clone();
        using var compile = new MIGraphXCompileOptions(options.NativePath, true, true, false);
        using var compileClone = compile.Clone();
        using var file = new MIGraphXFileOptions(options.NativePath);
        using var fileClone = file.Clone();
        using var onnx = new MIGraphXOnnxOptions(options.NativePath);
        onnx.SetInputParameterShape("input", new long[] { 1, 4 });
        using var onnxClone = onnx.Clone();
        Require(targetClone.Name == target.Name && compileClone.FastMath == compile.FastMath && fileClone.FileFormat == file.FileFormat, "Assign-to clone metadata differs.");
        using var program = ParseIdentity(onnx);
        using var programClone = program.Clone();
        Require(program.HasSameNativeContent(programClone), "Program assign-to clone differs from its source.");
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
        WriteStage(id, "program-dispose", "entered");
        program.Dispose();
        WriteStage(id, "main-print", "entered");
        main.Print();
        WriteStage(id, "child-print", "entered");
        child.Print();
        WriteStage(id, "teardown", "entered");
        main.Dispose();
        child.Dispose();
    }

    private void RunGraphEditing()
    {
        using var program = new MIGraphXProgram(options.NativePath);
        using var module = program.CreateModule("m12-edit");
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        using var parameter = module.AddParameter("input", shape);
        using var allocation = module.AddAllocation(shape);
        using var instructions = new MIGraphXInstructions(options.NativePath, new[] { parameter });
        using var returned = module.AddReturn(instructions);
        module.Print();
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

    internal static ProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Arguments must be --name value pairs.");
            values.Add(args[index], args[index + 1]);
        }
        string Required(string name) => values.TryGetValue(name, out var value) && value.Length != 0 ? value : throw new ArgumentException($"Missing required argument {name}.");
        string ExistingFile(string name) { var supplied = Required(name); var path = Path.GetFullPath(supplied); if (!Path.IsPathRooted(supplied) || !File.Exists(path)) throw new ArgumentException($"{name} must be an existing absolute file."); return path; }
        var source = Required("--source-sha");
        if (source.Length != 40 || source.Any(value => !Uri.IsHexDigit(value))) throw new ArgumentException("--source-sha must be a 40-character hexadecimal SHA.");
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
            CaseId = caseId
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
