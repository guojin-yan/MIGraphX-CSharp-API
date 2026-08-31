using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using JYPPX.ROCm.MIGraphXSharp;

namespace M12ProviderCallbackProbe;

internal static class Program
{
    public static int Main(string[] args)
    {
        var outputPath = TryGet(args, "--output") ?? Path.Combine(Directory.GetCurrentDirectory(), "provider-callback.json");
        var result = new ProbeResult { SchemaVersion = "1.0.0", Evidence = "runtime-candidate-executed-review-required" };
        var exitCode = 1;
        try
        {
            var options = ProbeOptions.Parse(args);
            result.SourceSha = options.SourceSha;
            result.ExpectedVersion = options.ExpectedVersion;
            result.ProviderFixture = options.ProviderFixture ? "fake-native-provider-dispatch" : "none";
            VerifyPackageIdentity(options);
            var state = new CallbackState();
            RunProviderProbe(options, state, result);
            if (state.ComputeShapeInvocations > 0 && result.ControlledFailure)
            {
                result.State = "callback-invoked-controlled-rejection";
                exitCode = 0;
            }
            else if (state.ComputeShapeInvocations == 0)
            {
                result.State = "callback-not-observed";
                exitCode = 2;
            }
            else
            {
                result.State = "callback-invoked-unexpected-outcome";
            }
            result.CallbackInvocations["computeShape"] = state.ComputeShapeInvocations;
        }
        catch (Exception exception)
        {
            result.State = "probe-failed";
            result.Error = exception.GetType().FullName + ": " + exception.Message;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) + Environment.NewLine);
        Console.WriteLine(File.ReadAllText(outputPath));
        return exitCode;
    }

    private static void VerifyPackageIdentity(ProbeOptions options)
    {
        if (!string.Equals(MIGraphXBuildInfo.PackageVersion, options.ExpectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Core package version is {MIGraphXBuildInfo.PackageVersion}, expected {options.ExpectedVersion}.");
        var informational = typeof(MIGraphXBuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var expectedInformational = options.ExpectedVersion + "+" + options.SourceSha;
        if (!string.Equals(informational, expectedInformational, StringComparison.Ordinal))
            throw new InvalidOperationException($"The core package informational version is {informational}, expected {expectedInformational}.");
    }

    private static void RunProviderProbe(ProbeOptions options, CallbackState state, ProbeResult result)
    {
        const string operationName = "m12_runtime_provider_callback_probe";
        result.OperationName = operationName;
        using var providerFixture = options.ProviderFixture ? new FakeProviderFixture(options.NativePath) : null;
        using var customOp = new MIGraphXExperimentalCustomOp(options.NativePath, operationName, state);
        customOp.SetComputeShape((_, _, _, _, _) =>
        {
            Interlocked.Increment(ref state.ComputeShapeInvocations);
            return MIGraphXStatus.UnknownError;
        });
        customOp.Register();
        result.RegistrationState = "registered";

        using var program = new MIGraphXProgram(options.NativePath);
        using var module = program.GetMainModule();
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        using var parameter = module.AddParameter("input", shape);
        using var arguments = new MIGraphXInstructions(options.NativePath, new[] { parameter });
        using var operation = MIGraphXOperation.Create(options.NativePath, operationName);

        MIGraphXInstruction? instruction = null;
        result.GraphState = "instruction-attempted";
        try
        {
            instruction = module.AddInstruction(operation, arguments);
            result.GraphState = "instruction-created";
        }
        catch (MIGraphXException exception)
        {
            result.GraphState = state.ComputeShapeInvocations > 0 ? "provider-dispatch-rejected" : "instruction-rejected";
            CaptureNativeFailure(result, exception);
        }

        if (instruction is not null)
        {
            using (instruction)
            using (var returnedArguments = new MIGraphXInstructions(options.NativePath, new[] { instruction }))
            using (var returned = module.AddReturn(returnedArguments))
            using (var target = new MIGraphXTarget(options.NativePath))
            using (var compileOptions = new MIGraphXCompileOptions(options.NativePath))
            {
                try
                {
                    program.Compile(target, compileOptions);
                    result.GraphState = "compiled";
                }
                catch (MIGraphXException exception)
                {
                    CaptureNativeFailure(result, exception);
                }
            }
        }

        result.ControlledFailure = result.NativeFailureStatus == (int)MIGraphXStatus.UnknownError;
    }

    private static void CaptureNativeFailure(ProbeResult result, MIGraphXException exception)
    {
        result.NativeFailureOperation = exception.Operation;
        result.NativeFailureStatus = exception.StatusCode;
        result.NativeFailureKnownStatus = exception.KnownStatus?.ToString();
    }

    private static string? TryGet(string[] args, string key)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], key, StringComparison.Ordinal)) return args[index + 1];
        }
        return null;
    }

    private sealed class CallbackState
    {
        internal int ComputeShapeInvocations;
    }

    private sealed class FakeProviderFixture : IDisposable
    {
        private readonly IntPtr library;
        private readonly EnableDispatch enableDispatch;

        internal FakeProviderFixture(string nativePath)
        {
            library = NativeLibrary.Load(nativePath);
            enableDispatch = Marshal.GetDelegateForFunctionPointer<EnableDispatch>(
                NativeLibrary.GetExport(library, "fake_enable_provider_callback_dispatch"));
            enableDispatch(1);
        }

        public void Dispose()
        {
            enableDispatch(0);
            NativeLibrary.Free(library);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EnableDispatch(int enabled);
    }
}

internal sealed class ProbeOptions
{
    internal string NativePath { get; private init; } = string.Empty;
    internal string SourceSha { get; private init; } = string.Empty;
    internal string ExpectedVersion { get; private init; } = string.Empty;
    internal bool ProviderFixture { get; private init; }

    internal static ProbeOptions Parse(string[] args)
    {
        var native = Required(args, "--native");
        var sourceSha = Required(args, "--source-sha");
        var version = Required(args, "--expected-version");
        if (!Path.IsPathRooted(native) || !File.Exists(native)) throw new FileNotFoundException("Native library is missing.", native);
        if (!System.Text.RegularExpressions.Regex.IsMatch(sourceSha, "^[a-f0-9]{40}$")) throw new ArgumentException("Source SHA must be a lowercase 40-character Git SHA.");
        if (!string.Equals(version, "0.0.0", StringComparison.Ordinal)) throw new ArgumentException("The provider callback probe is bound to package version 0.0.0.");
        return new ProbeOptions
        {
            NativePath = native,
            SourceSha = sourceSha,
            ExpectedVersion = version,
            ProviderFixture = args.Any(argument => string.Equals(argument, "--provider-fixture", StringComparison.Ordinal))
        };
    }

    private static string Required(string[] args, string key)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], key, StringComparison.Ordinal)) return args[index + 1];
        }
        throw new ArgumentException($"Missing required option {key}.");
    }
}

internal sealed class ProbeResult
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string SourceSha { get; set; } = string.Empty;
    public string ExpectedVersion { get; set; } = string.Empty;
    public string ProviderFixture { get; set; } = "none";
    public string OperationName { get; set; } = string.Empty;
    public string RegistrationState { get; set; } = string.Empty;
    public string GraphState { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public Dictionary<string, int> CallbackInvocations { get; } = new(StringComparer.Ordinal);
    public string? NativeFailureOperation { get; set; }
    public int? NativeFailureStatus { get; set; }
    public string? NativeFailureKnownStatus { get; set; }
    public bool ControlledFailure { get; set; }
    public string PromotionState { get; } = "not-requested";
    public string? Error { get; set; }
}
