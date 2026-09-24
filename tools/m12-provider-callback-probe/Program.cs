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
            result.ProbeKind = options.NumericalOutput ? "provider-custom-op-numerical-output" : "provider-callback-invocation";
            VerifyPackageIdentity(options);
            var state = new CallbackState();
            RunProviderProbe(options, state, result);
            if (options.NumericalOutput && state.ComputeShapeInvocations > 0 && state.ComputeInvocations > 0 && result.NumericalOutputMatched == true)
            {
                result.State = "callback-invoked-reference-matched";
                exitCode = 0;
            }
            else if (options.NumericalOutput)
            {
                result.State = state.ComputeShapeInvocations == 0 ? "callback-not-observed" : "numerical-output-not-matched";
            }
            else if (state.ComputeShapeInvocations > 0 && result.ControlledFailure)
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
            if (options.NumericalOutput)
            {
                result.CallbackInvocations["compute"] = state.ComputeInvocations;
                result.CallbackInvocations["runsOnOffloadTarget"] = state.RunsOnOffloadTargetInvocations;
            }
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
        using var nativeApi = options.NumericalOutput ? new NativeMigraphXApi(options.NativePath) : null;
        state.NativeApi = nativeApi;
        result.OperationName = operationName;
        using var providerFixture = options.ProviderFixture ? new FakeProviderFixture(options.NativePath) : null;
        using var customOp = new MIGraphXExperimentalCustomOp(options.NativePath, operationName, state);
        customOp.SetComputeShape((outputShape, _, _, _, inputs) =>
        {
            Interlocked.Increment(ref state.ComputeShapeInvocations);
            if (!options.NumericalOutput) return MIGraphXStatus.UnknownError;
            var api = state.NativeApi ?? throw new InvalidOperationException("The official C API helper was not initialized.");
            api.AssignShape(outputShape, api.GetShape(inputs));
            return MIGraphXStatus.Success;
        });
        if (options.NumericalOutput)
        {
            customOp.SetCompute((outputArgument, _, _, _, _, _, inputs) =>
            {
                Interlocked.Increment(ref state.ComputeInvocations);
                var api = state.NativeApi ?? throw new InvalidOperationException("The official C API helper was not initialized.");
                var inputArgument = api.GetArgument(inputs);
                var inputBuffer = api.GetArgumentBuffer(inputArgument);
                var outputBuffer = api.GetArgumentBuffer(outputArgument);
                var values = new float[] { 0, 0, 0, 0 };
                Marshal.Copy(inputBuffer, values, 0, values.Length);
                for (var index = 0; index < values.Length; index++) values[index] += 1.0f;
                Marshal.Copy(values, 0, outputBuffer, values.Length);
                return MIGraphXStatus.Success;
            });
            customOp.SetRunsOnOffloadTarget((output, _, _, _) =>
            {
                Interlocked.Increment(ref state.RunsOnOffloadTargetInvocations);
                if (output == IntPtr.Zero) throw new InvalidOperationException("MIGraphX supplied a null offload-target result pointer.");
                Marshal.WriteByte(output, 0);
                result.RunsOnOffloadTarget = false;
                return MIGraphXStatus.Success;
            });
        }
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

            if (options.NumericalOutput && result.GraphState == "compiled")
            {
                var inputs = new float[] { 0.25f, -1.0f, 2.0f, 9.0f };
                var expected = new float[] { 1.25f, 0.0f, 3.0f, 10.0f };
                using var argument = MIGraphXArgument.Create(options.NativePath, shape, inputs);
                using var parameterMap = new MIGraphXParameterMap(options.NativePath);
                parameterMap.Add("input", argument);
                try
                {
                    using var outputs = program.Run(parameterMap);
                    var actual = outputs[0].ToArray<float>();
                    result.InputValues = inputs;
                    result.ExpectedOutput = expected;
                    result.ActualOutput = actual;
                    result.NumericalOutputMatched = actual.SequenceEqual(expected);
                    result.GraphState = "executed";
                }
                catch (MIGraphXException exception)
                {
                    result.GraphState = "run-failed";
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
        internal int ComputeInvocations;
        internal int RunsOnOffloadTargetInvocations;
        internal NativeMigraphXApi? NativeApi;
    }

    private sealed class NativeMigraphXApi : IDisposable
    {
        private readonly IntPtr library;
        private readonly ShapeAssignTo shapeAssignTo;
        private readonly CollectionGet shapesGet;
        private readonly CollectionGet argumentsGet;
        private readonly ArgumentBuffer argumentBuffer;

        internal NativeMigraphXApi(string nativePath)
        {
            library = NativeLibrary.Load(nativePath);
            try
            {
                shapeAssignTo = Load<ShapeAssignTo>("migraphx_shape_assign_to");
                shapesGet = Load<CollectionGet>("migraphx_shapes_get");
                argumentsGet = Load<CollectionGet>("migraphx_arguments_get");
                argumentBuffer = Load<ArgumentBuffer>("migraphx_argument_buffer");
            }
            catch
            {
                NativeLibrary.Free(library);
                throw;
            }
        }

        internal IntPtr GetShape(IntPtr shapes) => GetBorrowedHandle(slot => shapesGet(slot, shapes, UIntPtr.Zero), "migraphx_shapes_get");
        internal IntPtr GetArgument(IntPtr arguments) => GetBorrowedHandle(slot => argumentsGet(slot, arguments, UIntPtr.Zero), "migraphx_arguments_get");

        internal void AssignShape(IntPtr output, IntPtr input)
        {
            if (output == IntPtr.Zero || input == IntPtr.Zero) throw new InvalidOperationException("MIGraphX supplied a null shape handle to the callback.");
            Check(shapeAssignTo(output, input), "migraphx_shape_assign_to");
        }

        internal IntPtr GetArgumentBuffer(IntPtr argument)
        {
            if (argument == IntPtr.Zero) throw new InvalidOperationException("MIGraphX supplied a null argument handle to the callback.");
            var buffer = GetBorrowedHandle(slot => argumentBuffer(slot, argument), "migraphx_argument_buffer");
            return buffer != IntPtr.Zero ? buffer : throw new InvalidOperationException("MIGraphX returned a null host argument buffer.");
        }

        public void Dispose() => NativeLibrary.Free(library);

        private T Load<T>(string name) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        private static IntPtr GetBorrowedHandle(Func<IntPtr, int> invoke, string operation)
        {
            var slot = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(slot, IntPtr.Zero);
                Check(invoke(slot), operation);
                var value = Marshal.ReadIntPtr(slot);
                return value != IntPtr.Zero ? value : throw new InvalidOperationException($"{operation} returned a null handle.");
            }
            finally { Marshal.FreeHGlobal(slot); }
        }

        private static void Check(int status, string operation)
        {
            if (status != (int)MIGraphXStatus.Success)
                throw new InvalidOperationException($"{operation} returned native status {status}.");
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ShapeAssignTo(IntPtr output, IntPtr input);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CollectionGet(IntPtr output, IntPtr collection, UIntPtr index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ArgumentBuffer(IntPtr output, IntPtr argument);
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
    internal bool NumericalOutput { get; private init; }

    internal static ProbeOptions Parse(string[] args)
    {
        var native = Required(args, "--native");
        var sourceSha = Required(args, "--source-sha");
        var version = Required(args, "--expected-version");
        var mode = Optional(args, "--mode") ?? "reachability";
        if (!Path.IsPathRooted(native) || !File.Exists(native)) throw new FileNotFoundException("Native library is missing.", native);
        if (!System.Text.RegularExpressions.Regex.IsMatch(sourceSha, "^[a-f0-9]{40}$")) throw new ArgumentException("Source SHA must be a lowercase 40-character Git SHA.");
        if (!string.Equals(version, "0.0.0", StringComparison.Ordinal)) throw new ArgumentException("The provider callback probe is bound to package version 0.0.0.");
        if (mode != "reachability" && mode != "numerical-output") throw new ArgumentException("Probe mode must be reachability or numerical-output.");
        var providerFixture = args.Any(argument => string.Equals(argument, "--provider-fixture", StringComparison.Ordinal));
        if (mode == "numerical-output" && providerFixture) throw new ArgumentException("The numerical-output mode requires the official provider, not the fake-native fixture.");
        return new ProbeOptions
        {
            NativePath = native,
            SourceSha = sourceSha,
            ExpectedVersion = version,
            ProviderFixture = providerFixture,
            NumericalOutput = mode == "numerical-output"
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

    private static string? Optional(string[] args, string key)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], key, StringComparison.Ordinal)) return args[index + 1];
        }
        return null;
    }
}

internal sealed class ProbeResult
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string ProbeKind { get; set; } = string.Empty;
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
    public float[]? InputValues { get; set; }
    public float[]? ExpectedOutput { get; set; }
    public float[]? ActualOutput { get; set; }
    public bool? NumericalOutputMatched { get; set; }
    public bool? RunsOnOffloadTarget { get; set; }
    public string PromotionState { get; } = "not-requested";
    public string? Error { get; set; }
}
