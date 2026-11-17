using System.Runtime.InteropServices;
using System.Text.Json;
using JYPPX.ROCm.HipSharp;
using JYPPX.ROCm.HipSharp.Rtc;
using JYPPX.ROCm.HipSharp.Types;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using JYPPX.ROCm.MIGraphXSharp;

internal static class GpuCallbackRegression
{
    internal static void Run(string native, string hipPath, string output, Action<string, Action> check,
        Action<MIGraphXProgram, bool> compile)
    {
        check("gpu-custom-kernel-stream-alias-and-registry-lifetime", () =>
        {
            using var api = new CallbackApi(native, hipPath);
            using var hip = new HipRuntime(hipPath);
            using var stream = hip.CreateStream(HipStreamFlags.NonBlocking);
            var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4096 });
            var name = "managed_gpu_increment_" + Guid.NewGuid().ToString("N");
            var calls = 0;
            var callbackQueues = new HashSet<IntPtr>();
            using (var op = new MIGraphXExperimentalCustomOp(native, name))
            {
                op.SetComputeShape((result, _, _, _, inputs) => { api.Check(api.AssignShape(result, api.Get(api.ShapesGet, inputs))); return MIGraphXStatus.Success; });
                op.SetOutputAlias((indices, size, _, _, _, _) =>
                {
                    Marshal.WriteIntPtr(indices, IntPtr.Zero); Marshal.WriteIntPtr(size, new IntPtr(1));
                    return MIGraphXStatus.Success;
                });
                op.SetRunsOnOffloadTarget((result, _, _, _) => { Marshal.WriteByte(result, 1); return MIGraphXStatus.Success; });
                op.SetCompute((result, _, _, _, context, _, inputs) =>
                {
                    var argument = api.Get(api.ArgumentsGet, inputs);
                    var queue = api.GetPointer(api.ContextQueue, context);
                    // 2.15 bridges its own context queue to the caller with events;
                    // it need not equal a different program's queue or the client stream.
                    callbackQueues.Add(queue);
                    // The buffer is device memory. Pass it to HIP; never dereference it on the CPU.
                    api.Launch(api.GetPointer(api.ArgumentBuffer, argument), queue);
                    api.Check(api.AssignArgument(result, argument));
                    calls++;
                    return MIGraphXStatus.Success;
                });
                using var clone = op.Clone(); clone.Register();
            }
            GC.Collect(); GC.WaitForPendingFinalizers();
            using var program = Build(native, name, shape);
            compile(program, true);
            var values = Enumerable.Range(0, 4096).Select(i => (i % 53 - 27) * .25f).ToArray();
            var expected = values.Select(value => Math.Max(0, value + 1)).ToArray();
            using var input = MIGraphXArgument.Create(native, shape, values);
            using var parameters = new MIGraphXParameterMap(native); parameters.Add("input", input);
            using (var result = program.Run(parameters)) RuntimeRegression.Equal(expected, result[0].ToArray<float>());
            using (var pending = program.RunHostAsync(parameters, stream))
            { pending.Synchronize(); RuntimeRegression.Equal(expected, pending.Outputs[0].ToArray<float>()); }
            using var deviceProgram = Build(native, name, shape); compile(deviceProgram, false);
            using var deviceParameters = new SoakRegression.DeviceParameters(deviceProgram, hip);
            var device = deviceParameters.Input;
            using var destination = hip.Allocate(4096 * sizeof(float));
            var bytes = new byte[4096 * sizeof(float)]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); device.CopyFrom(bytes);
            using (var pending = deviceProgram.RunDeviceIntoAsync(deviceParameters.Inputs, new[] { destination }, stream))
            {
                deviceParameters.Dispose(); deviceProgram.Dispose();
                pending.Synchronize();
            }
            destination.CopyTo(bytes); var actual = new float[4096]; Buffer.BlockCopy(bytes, 0, actual, 0, bytes.Length);
            RuntimeRegression.Equal(expected, actual);
            if (calls != 3) throw new Exception("Expected three GPU compute callbacks, got " + calls);
            File.WriteAllText(Path.Combine(output, "gpu-custom-kernel.json"), JsonSerializer.Serialize(new {
                architecture = api.Architecture, codeSha256 = api.CodeSha256, calls, elements = values.Length,
                hostSynchronous = true, hostAsync = true, deviceAsync = true, callerStreamNonBlocking = true,
                callbackQueueUsedForLaunch = true, distinctCallbackQueues = callbackQueues.Count,
                inputAndProgramDisposedBeforeSynchronization = true
            }, new JsonSerializerOptions { WriteIndented = true }));
        });
        check("gpu-custom-compute-exception-contained", () =>
        {
            using var api = new CallbackApi(native, hipPath, compileKernel: false);
            var name = "managed_gpu_failure_" + Guid.NewGuid().ToString("N");
            var calls = 0;
            using (var op = new MIGraphXExperimentalCustomOp(native, name))
            {
                op.SetComputeShape((result, _, _, _, inputs) => { api.Check(api.AssignShape(result, api.Get(api.ShapesGet, inputs))); return MIGraphXStatus.Success; });
                op.SetOutputAlias((indices, size, _, _, _, _) => { Marshal.WriteIntPtr(indices, IntPtr.Zero); Marshal.WriteIntPtr(size, new IntPtr(1)); return MIGraphXStatus.Success; });
                op.SetRunsOnOffloadTarget((result, _, _, _) => { Marshal.WriteByte(result, 1); return MIGraphXStatus.Success; });
                op.SetCompute((_, _, _, _, _, _, _) => { calls++; throw new InvalidOperationException("controlled GPU compute failure"); });
                op.Register();
            }
            var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4096 });
            using var program = Build(native, name, shape); compile(program, true);
            using var input = MIGraphXArgument.Create(native, shape, new float[4096]);
            using var parameters = new MIGraphXParameterMap(native); parameters.Add("input", input);
            RuntimeRegression.Expect<MIGraphXException>(() => { using var result = program.Run(parameters); });
            if (calls != 1) throw new Exception("GPU compute callback was not reached exactly once.");
            using var recovery = SoakRegression.CreateRelu(native, 4096); compile(recovery, true);
            using var outputs = recovery.Run(parameters); RuntimeRegression.Equal(new float[4096], outputs[0].ToArray<float>());
        });
    }

    private static MIGraphXProgram Build(string native, string name, MIGraphXShape shape)
    {
        var program = new MIGraphXProgram(native);
        try
        {
            using var main = program.GetMainModule(); using var input = main.AddParameter("input", shape);
            using var args = new MIGraphXInstructions(native, new[] { input }); using var op = MIGraphXOperation.Create(native, name);
            using var custom = main.AddInstruction(op, args);
            using var reluArgs = new MIGraphXInstructions(native, new[] { custom }); using var relu = MIGraphXOperation.Create(native, "relu");
            using var activated = main.AddInstruction(relu, reluArgs); using var returns = new MIGraphXInstructions(native, new[] { activated });
            using var returned = main.AddReturn(returns); return program;
        }
        catch { program.Dispose(); throw; }
    }

    // These delegates describe official MIGraphX C and HIP C entry points only.
    // Borrowed context streams and argument buffers are never wrapped as owners.
    private sealed class CallbackApi : IDisposable
    {
        private readonly IntPtr library, hipLibrary;
        private IntPtr module, function;
        internal readonly Assign AssignShape, AssignArgument;
        internal readonly CollectionGet ShapesGet, ArgumentsGet;
        internal readonly PointerGet ArgumentBuffer, ContextQueue;
        private readonly ModuleUnload? unload;
        private readonly ModuleLaunch? launch;
        internal string Architecture { get; } = Environment.GetEnvironmentVariable("MIGRAPHX_TEST_GPU_ARCHITECTURE") ?? "gfx1100";
        internal string? CodeSha256 { get; }
        internal CallbackApi(string native, string hipPath, bool compileKernel = true)
        {
            library = NativeLibrary.Load(native);
            AssignShape = Load<Assign>(library, "migraphx_shape_assign_to"); AssignArgument = Load<Assign>(library, "migraphx_argument_assign_to");
            ShapesGet = Load<CollectionGet>(library, "migraphx_shapes_get"); ArgumentsGet = Load<CollectionGet>(library, "migraphx_arguments_get");
            ArgumentBuffer = Load<PointerGet>(library, "migraphx_argument_buffer"); ContextQueue = Load<PointerGet>(library, "migraphx_context_get_queue");
            if (!compileKernel) return;
            try
            {
                var rtcPath = OperatingSystem.IsWindows()
                    ? Directory.GetFiles(Path.GetDirectoryName(hipPath)!, "hiprtc*.dll").Single(path => !Path.GetFileName(path).Contains("builtins", StringComparison.OrdinalIgnoreCase))
                    : Path.Combine(Path.GetDirectoryName(hipPath)!, "libhiprtc.so");
                var rtc = new HipRtc(rtcPath);
                using var source = rtc.CreateProgram("extern \"C\" __global__ void increment(float* data, int count) { int i = blockIdx.x * blockDim.x + threadIdx.x; if(i < count) data[i] += 1.0f; }");
                var compilation = source.Compile(new[] { "--gpu-architecture=" + Architecture, "-O2" });
                CodeSha256 = compilation.CodeSha256;
                hipLibrary = NativeLibrary.Load(hipPath);
                unload = Load<ModuleUnload>(hipLibrary, "hipModuleUnload"); launch = Load<ModuleLaunch>(hipLibrary, "hipModuleLaunchKernel");
                var image = GCHandle.Alloc(compilation.GetCodeObject(), GCHandleType.Pinned);
                try { Check(Load<ModuleLoad>(hipLibrary, "hipModuleLoadData")(out module, image.AddrOfPinnedObject())); }
                finally { image.Free(); }
                Check(Load<ModuleFunction>(hipLibrary, "hipModuleGetFunction")(out function, module, "increment"));
            }
            catch { Dispose(); throw; }
        }
        private static T Load<T>(IntPtr library, string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        internal void Check(int status) { if (status != 0) throw new Exception("Native callback helper status " + status); }
        internal IntPtr Get(CollectionGet get, IntPtr collection) { Check(get(out var result, collection, UIntPtr.Zero)); return result; }
        internal IntPtr GetPointer(PointerGet get, IntPtr value) { Check(get(out var result, value)); return result; }
        internal void Launch(IntPtr device, IntPtr stream)
        {
            var arguments = Marshal.AllocHGlobal(4 * IntPtr.Size);
            try
            {
                var pointerSlot = IntPtr.Add(arguments, 2 * IntPtr.Size); var countSlot = IntPtr.Add(arguments, 3 * IntPtr.Size);
                Marshal.WriteIntPtr(pointerSlot, device); Marshal.WriteInt32(countSlot, 4096);
                Marshal.WriteIntPtr(arguments, pointerSlot); Marshal.WriteIntPtr(arguments, IntPtr.Size, countSlot);
                Check(launch!(function, 16, 1, 1, 256, 1, 1, 0, stream, arguments, IntPtr.Zero));
            }
            finally { Marshal.FreeHGlobal(arguments); }
        }
        public void Dispose()
        {
            if (module != IntPtr.Zero) { unload!(module); module = IntPtr.Zero; }
            if (hipLibrary != IntPtr.Zero) NativeLibrary.Free(hipLibrary);
            NativeLibrary.Free(library);
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Assign(IntPtr destination, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CollectionGet(out IntPtr value, IntPtr collection, UIntPtr index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PointerGet(out IntPtr value, IntPtr argument);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ModuleLoad(out IntPtr module, IntPtr image);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ModuleUnload(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ModuleFunction(out IntPtr function, IntPtr module, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ModuleLaunch(IntPtr function, uint gx, uint gy, uint gz, uint bx, uint by, uint bz, uint shared, IntPtr stream, IntPtr arguments, IntPtr extra);
    }
}
