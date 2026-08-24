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
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
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
    private readonly ProbeOptions options;
    private readonly ProbeReport report;
    private readonly MIGraphXShape scalar = MIGraphXShape.CreateScalar(MIGraphXShapeDataType.Float32);

    internal ProbeRunner(ProbeOptions options, ProbeReport report)
    {
        this.options = options;
        this.report = report;
        if (!File.Exists(options.NativePath)) throw new FileNotFoundException("Native library is missing.", options.NativePath);
        if (!File.Exists(options.IdentityFixture)) throw new FileNotFoundException("Identity fixture is missing.", options.IdentityFixture);
        const string expectedIdentitySha = "0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9";
        if (Program.HashFile(options.IdentityFixture) != expectedIdentitySha) throw new InvalidOperationException("Identity fixture SHA-256 mismatch.");
        Directory.CreateDirectory(options.RecordDirectory);
        Directory.CreateDirectory(Path.Combine(options.RecordDirectory, "raw"));
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
        var strided = MIGraphXShape.CreateWithStrides(MIGraphXShapeDataType.Float32, new long[] { 2, 2 }, new long[] { 1, 2 });
        Require(strided.Rank == 2 && strided.Strides.SequenceEqual(new long[] { 1, 2 }), "Explicit strides were not preserved.");
        using var empty = MIGraphXArgument.CreateEmpty(options.NativePath, scalar);
        using var generated = MIGraphXArgument.Generate(options.NativePath, new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 }), 17);
        using var clone = generated.Clone();
        Require(generated.HasSameNativeContent(clone), "Generated argument clone differs from its source.");
        Require(empty.Shape.HasSameNativeContent(scalar), "Empty argument shape snapshot differs.");
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
        var program = ParseIdentity(new MIGraphXOnnxOptions(options.NativePath));
        using var main = program.GetMainModule();
        using var child = program.CreateModule("m12-child");
        using var context = program.GetExperimentalContext();
        program.Dispose();
        main.Print();
        child.Print();
        context.Finish();
        Require(context.Queue != IntPtr.Zero, "Context queue was null after parent program disposal.");
        main.Dispose();
        child.Dispose();
        context.Dispose();
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
        var program = ParseIdentity(new MIGraphXOnnxOptions(options.NativePath));
        using var context = program.GetExperimentalContext();
        program.Dispose();
        context.Finish();
        Require(context.Queue != IntPtr.Zero, "Context queue was null.");
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
        string ExistingFile(string name) { var path = Path.GetFullPath(Required(name)); if (!Path.IsPathRooted(Required(name)) || !File.Exists(path)) throw new ArgumentException($"{name} must be an existing absolute file."); return path; }
        var source = Required("--source-sha");
        if (source.Length != 40 || source.Any(value => !Uri.IsHexDigit(value))) throw new ArgumentException("--source-sha must be a 40-character hexadecimal SHA.");
        var record = Path.GetFullPath(Required("--record"));
        Directory.CreateDirectory(record);
        var output = Path.GetFullPath(Required("--output"));
        var prefix = record.EndsWith(Path.DirectorySeparatorChar) ? record : record + Path.DirectorySeparatorChar;
        if (!output.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("--output must be inside --record.");
        values.TryGetValue("--case", out var caseId);
        return new ProbeOptions { NativePath = ExistingFile("--native"), IdentityFixture = ExistingFile("--identity"), RecordDirectory = record, OutputPath = output, SourceSha = source.ToLowerInvariant(), ExpectedVersion = Required("--expected-version"), CaseId = caseId };
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
    public string? Exception { get; set; }
    public string? Message { get; set; }
}

internal sealed record ProbeCase(string Id, string State, TimeSpan Duration, string? Exception);
