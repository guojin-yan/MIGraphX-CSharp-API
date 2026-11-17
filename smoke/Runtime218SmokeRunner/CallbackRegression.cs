using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp;

internal static class CallbackRegression
{
    internal static void Run(string native, string hipPath, string output, Action<string, Action> check)
    {
        check("custom-op-shape-exception-contained", () =>
        {
            var calls = 0;
            var name = "managed_rejection_" + Guid.NewGuid().ToString("N");
            using var op = new MIGraphXExperimentalCustomOp(native, name);
            op.SetComputeShape((_, _, _, _, _) => { calls++; throw new InvalidOperationException("controlled shape failure"); });
            op.Register();
            using var program = new MIGraphXProgram(native); using var module = program.GetMainModule();
            using var input = module.AddParameter("input", new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 }));
            using var args = new MIGraphXInstructions(native, new[] { input }); using var operation = MIGraphXOperation.Create(native, name);
            RuntimeRegression.Expect<MIGraphXException>(() => { using var instruction = module.AddInstruction(operation, args); });
            if (calls == 0) throw new Exception("Shape callback was not reached.");
        });
        check("custom-op-reference-numerical-and-registry-lifetime", () =>
        {
            using var api = new CallbackApi(native);
            var name = "managed_increment_" + Guid.NewGuid().ToString("N");
            var calls = 0;
            using (var op = new MIGraphXExperimentalCustomOp(native, name))
            {
                op.SetComputeShape((result, _, _, _, inputs) => { api.Check(api.AssignShape(result, api.Get(api.ShapesGet, inputs))); return MIGraphXStatus.Success; });
                op.SetCompute((result, _, _, _, _, shape, inputs) =>
                {
                    calls++;
                    var argument = api.Get(api.ArgumentsGet, inputs);
                    var values = new float[4]; Marshal.Copy(api.GetBuffer(argument), values, 0, 4);
                    api.AssignOutput(result, shape, values.Select(value => value + 1).ToArray());
                    return MIGraphXStatus.Success;
                });
                op.SetOutputAlias((_, size, _, _, _, _) => { Marshal.WriteIntPtr(size, IntPtr.Zero); return MIGraphXStatus.Success; });
                op.SetRunsOnOffloadTarget((value, _, _, _) => { Marshal.WriteByte(value, 0); return MIGraphXStatus.Success; });
                using var clone = op.Clone(); clone.Register();
            }
            GC.Collect(); GC.WaitForPendingFinalizers();
            using var program = new MIGraphXProgram(native); using var module = program.GetMainModule();
            var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
            using var input = module.AddParameter("input", shape);
            using var args = new MIGraphXInstructions(native, new[] { input }); using var operation = MIGraphXOperation.Create(native, name);
            using var instruction = module.AddInstruction(operation, args);
            using var returns = new MIGraphXInstructions(native, new[] { instruction }); using var returned = module.AddReturn(returns);
            using var target = new MIGraphXTarget(native, "ref"); using var options = new MIGraphXCompileOptions(native); program.Compile(target, options);
            using var argument = MIGraphXArgument.Create(native, shape, new[] { .25f, -1f, 2f, 9f });
            using var parameters = new MIGraphXParameterMap(native); parameters.Add("input", argument);
            using var result = program.Run(parameters);
            RuntimeRegression.Equal(new[] { 1.25f, 0f, 3f, 10f }, result[0].ToArray<float>());
            if (calls == 0) throw new Exception("Compute callback was not reached.");
        });
    }

    // Raw callback pointers are borrowed. These helpers use only the official C API
    // and host buffers on the reference target; GPU pointers are never dereferenced.
    private sealed class CallbackApi : IDisposable
    {
        private readonly IntPtr library;
        private readonly List<IntPtr> buffers = new();
        internal readonly Assign AssignShape;
        internal readonly CollectionGet ShapesGet, ArgumentsGet;
        private readonly BufferGet bufferGet;
        private readonly Create create;
        private readonly Assign assign;
        private readonly Destroy destroy;
        internal CallbackApi(string native)
        {
            library = NativeLibrary.Load(native);
            AssignShape = Load<Assign>("migraphx_shape_assign_to"); ShapesGet = Load<CollectionGet>("migraphx_shapes_get");
            ArgumentsGet = Load<CollectionGet>("migraphx_arguments_get"); bufferGet = Load<BufferGet>("migraphx_argument_buffer");
            create = Load<Create>("migraphx_argument_create"); assign = Load<Assign>("migraphx_argument_assign_to"); destroy = Load<Destroy>("migraphx_argument_destroy");
        }
        private T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        internal void Check(int status) { if (status != 0) throw new Exception("Callback helper status " + status); }
        internal IntPtr Get(CollectionGet get, IntPtr collection) { Check(get(out var result, collection, UIntPtr.Zero)); return result; }
        internal IntPtr GetBuffer(IntPtr argument) { Check(bufferGet(out var result, argument)); return result; }
        internal void AssignOutput(IntPtr result, IntPtr shape, float[] values)
        {
            var buffer = Marshal.AllocHGlobal(values.Length * sizeof(float)); buffers.Add(buffer); Marshal.Copy(values, 0, buffer, values.Length);
            Check(create(out var argument, shape, buffer));
            try { Check(assign(result, argument)); } finally { Check(destroy(argument)); }
        }
        public void Dispose() { foreach (var buffer in buffers) Marshal.FreeHGlobal(buffer); NativeLibrary.Free(library); }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Assign(IntPtr destination, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CollectionGet(out IntPtr result, IntPtr collection, UIntPtr index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BufferGet(out IntPtr result, IntPtr argument);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(out IntPtr result, IntPtr shape, IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(IntPtr argument);
    }
}
