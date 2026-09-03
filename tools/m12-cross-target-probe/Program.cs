using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using JYPPX.ROCm.MIGraphXSharp;

namespace M12CrossTargetProbe;

internal static class Program
{
    internal const string Evidence = "runtime-candidate-executed-review-required";
    internal const string CaseId = "m12-cross-target-abi";
    private const string IdentitySha256 = "0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9";

    public static int Main(string[] args)
    {
        ProbeOptions? options = null;
        var report = new ProbeReport();
        try
        {
            options = ProbeOptions.Parse(args);
            report.SourceSha = options.SourceSha;
            report.PackageVersion = MIGraphXBuildInfo.PackageVersion;
            report.TargetFramework = GetTargetFramework();
            report.RuntimeFramework = RuntimeInformation.FrameworkDescription;
            report.ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
            report.InteropStrategy = GetCoreInteropStrategy();

            VerifyIdentity(options, report);
            Execute(options, report);
            report.State = "executed";
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
        var output = options?.OutputPath ?? "m12-cross-target.json";
        File.WriteAllText(output, json + Environment.NewLine);
        Console.WriteLine(json);
        return report.State == "executed" ? 0 : 1;
    }

    private static void VerifyIdentity(ProbeOptions options, ProbeReport report)
    {
        if (!string.Equals(report.PackageVersion, options.ExpectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Core package version is {report.PackageVersion}, expected {options.ExpectedVersion}.");

        var coreAssembly = typeof(MIGraphXBuildInfo).Assembly;
        var informational = coreAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        if (!informational.Contains(options.SourceSha, StringComparison.Ordinal))
            throw new InvalidOperationException("The core package informational version is not bound to the requested source SHA.");

        report.IdentityFixtureSha256 = HashFile(options.IdentityFixture);
        if (!string.Equals(report.IdentityFixtureSha256, IdentitySha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Identity fixture SHA-256 mismatch.");

        var expectedStrategy = report.TargetFramework == ".NETCoreApp,Version=v3.1" ? "DllImport" : "LibraryImport";
        if (!string.Equals(report.InteropStrategy, expectedStrategy, StringComparison.Ordinal))
            throw new InvalidOperationException($"Core interop strategy is {report.InteropStrategy}, expected {expectedStrategy}.");
    }

    private static void Execute(ProbeOptions options, ProbeReport report)
    {
        var expected = new[] { 0.25f, -1f, 2f, 9f };
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        using var onnx = new MIGraphXOnnxOptions(options.NativePath);
        onnx.SetInputParameterShape("input", new long[] { 1, 4 });
        using var program = MIGraphXProgram.ParseOnnxFile(options.IdentityFixture, onnx);
        using var target = new MIGraphXTarget(options.NativePath);
        using var compile = new MIGraphXCompileOptions(options.NativePath);
        program.Compile(target, compile);

        using var input = MIGraphXArgument.Create(options.NativePath, shape, expected);
        using var parameters = new MIGraphXParameterMap(options.NativePath);
        parameters.Add("input", input);
        using var outputs = program.Run(parameters);
        var actual = outputs.SelectMany(output => output.ToArray<float>()).ToArray();
        report.ReferenceMatched = actual.SequenceEqual(expected);
        if (!report.ReferenceMatched) throw new InvalidOperationException("Identity output differs from the reviewed reference.");

        var attributes = MIGraphXOperationAttributes.ForReshape(1, 4);
        using var operation = MIGraphXOperation.Create(options.NativePath, "reshape", attributes);
        using var operationClone = operation.Clone();
        report.OperationName = operation.Name;
        report.OperationCloneName = operationClone.Name;
        if (report.OperationName != "reshape" || report.OperationCloneName != "reshape")
            throw new InvalidOperationException("M12 materialized operation creation or clone differs across the target framework.");
    }

    private static string GetTargetFramework()
        => typeof(Program).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .Cast<TargetFrameworkAttribute>()
            .Single()
            .FrameworkName;

    private static string GetCoreInteropStrategy()
    {
        var type = typeof(MIGraphXBuildInfo).Assembly.GetType("JYPPX.ROCm.MIGraphXSharp.Interop.InteropCompilationProbe", throwOnError: true)!;
        var field = type.GetField("Strategy", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Core interop strategy marker is missing.");
        return field.GetRawConstantValue() as string
            ?? throw new InvalidOperationException("Core interop strategy marker is invalid.");
    }

    private static string HashFile(string path)
    {
        using var algorithm = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
}

internal sealed class ProbeOptions
{
    internal string NativePath { get; private set; } = string.Empty;
    internal string IdentityFixture { get; private set; } = string.Empty;
    internal string OutputPath { get; private set; } = string.Empty;
    internal string SourceSha { get; private set; } = string.Empty;
    internal string ExpectedVersion { get; private set; } = string.Empty;

    internal static ProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Arguments must be --name value pairs.");
            values.Add(args[index], args[index + 1]);
        }

        string Required(string name) => values.TryGetValue(name, out var value) && value.Length != 0
            ? value
            : throw new ArgumentException($"Missing required argument {name}.");
        string ExistingAbsoluteFile(string name)
        {
            var value = Required(name);
            if (!Path.IsPathRooted(value) || !File.Exists(value)) throw new ArgumentException($"{name} must be an existing absolute file.");
            return Path.GetFullPath(value);
        }

        var output = Required("--output");
        if (!Path.IsPathRooted(output) || !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(output))))
            throw new ArgumentException("--output must be an absolute path in an existing directory.");
        var sourceSha = Required("--source-sha");
        if (sourceSha.Length != 40 || sourceSha.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("--source-sha must be a 40-character hexadecimal SHA.");
        if (values.Count != 5) throw new ArgumentException("Unexpected cross-target probe argument.");

        return new ProbeOptions
        {
            NativePath = ExistingAbsoluteFile("--native"),
            IdentityFixture = ExistingAbsoluteFile("--identity"),
            OutputPath = Path.GetFullPath(output),
            SourceSha = sourceSha.ToLowerInvariant(),
            ExpectedVersion = Required("--expected-version")
        };
    }
}

internal sealed class ProbeReport
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public string Evidence { get; set; } = Program.Evidence;
    public string CaseId { get; set; } = Program.CaseId;
    public string State { get; set; } = "failed";
    public string SourceSha { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public string RuntimeFramework { get; set; } = string.Empty;
    public string ProcessArchitecture { get; set; } = string.Empty;
    public string InteropStrategy { get; set; } = string.Empty;
    public string IdentityFixtureSha256 { get; set; } = string.Empty;
    public bool ReferenceMatched { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public string OperationCloneName { get; set; } = string.Empty;
    public DateTimeOffset CompletedUtc { get; set; }
    public string? Exception { get; set; }
    public string? Message { get; set; }
}
