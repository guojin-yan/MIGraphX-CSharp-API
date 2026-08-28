using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp;
using JYPPX.ROCm.MIGraphXSharp.Interop;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M12LocalInterfaceTests
{
    [Fact]
    public void ShapeAndArgumentFactoriesPreserveDetachedValueSemantics()
    {
        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();

        var scalar = MIGraphXShape.CreateScalar(MIGraphXShapeDataType.Float32);
        Assert.Equal(0, scalar.Ndim);
        Assert.Equal(1, scalar.ElementCount);
        Assert.Equal(4, scalar.ByteCount);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MIGraphXShape((MIGraphXShapeDataType)999, new long[] { 1 }));

        var transposed = MIGraphXShape.CreateWithStrides(
            MIGraphXShapeDataType.Float32,
            new long[] { 2, 3 },
            new long[] { 1, 2 });
        Assert.Equal(2, transposed.Rank);
        Assert.Equal(3, transposed.Index(1));
        Assert.False(transposed.IsStandard);
        Assert.True(transposed.HasSameNativeContent(transposed.Clone()));

        var dynamicShape = MIGraphXShape.CreateDynamic(
            MIGraphXShapeDataType.Float32,
            new[]
            {
                MIGraphXDynamicDimension.Fixed(1),
                MIGraphXDynamicDimension.Range(2, 8, new long[] { 4, 6 }),
            });
        var dimensions = dynamicShape.GetDynamicDimensionCollection();
        Assert.True(dimensions.HasSameNativeContent(dimensions.Clone()));

        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 2, 2 });
        var file = Path.Combine(Path.GetTempPath(), $"migraphx-m12-argument-{Guid.NewGuid():N}.bin");
        try
        {
            using (var empty = MIGraphXArgument.CreateEmpty(nativePath, shape))
            using (var generated = MIGraphXArgument.Generate(nativePath, shape, 123))
            using (var repeated = MIGraphXArgument.Generate(nativePath, shape, 123))
            using (var clone = generated.Clone())
            {
                Assert.Equal(new float[4], empty.ToArray<float>());
                Assert.Equal(generated.ToArray<float>(), repeated.ToArray<float>());
                Assert.Equal(generated.ToArray<float>(), clone.ToArray<float>());
                generated.Save(file);
                using var loaded = MIGraphXArgument.Load(nativePath, file);
                Assert.Equal(generated.ToArray<float>(), loaded.ToArray<float>());
            }
        }
        finally
        {
            File.Delete(file);
        }

        var borrowedPointer = Marshal.AllocHGlobal(16);
        try
        {
            using (var borrowed = MIGraphXArgument.CreateExternal(NativeRuntime.Load(nativePath), shape, borrowedPointer))
            {
                Assert.Throws<NotSupportedException>(() => borrowed.Clone());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(borrowedPointer);
        }

        AssertNoNativeLeaks(controls);
    }

    [Fact]
    public void AssignToClonesPrecreateDestinationsAndReleaseFailedCopies()
    {
        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();

        using (var target = new MIGraphXTarget(nativePath, "gpu"))
        using (var targetClone = target.Clone())
        using (var compileOptions = new MIGraphXCompileOptions(nativePath, offloadCopy: false, fastMath: true, exhaustiveTune: true))
        using (var compileClone = compileOptions.Clone())
        using (var onnxOptions = new MIGraphXOnnxOptions(nativePath))
        using (var onnxClone = onnxOptions.Clone())
        using (var fileOptions = new MIGraphXFileOptions(nativePath, "msgpack"))
        using (var fileClone = fileOptions.Clone())
        using (var program = new MIGraphXProgram(nativePath))
        using (var programClone = program.Clone())
        using (var tfOptions = new MIGraphXTfOptions(nativePath))
        using (var tfClone = tfOptions.Clone())
        using (var names = new MIGraphXQuantizeOpNames(nativePath))
        using (var namesClone = names.Clone())
        using (var int8 = new MIGraphXQuantizeInt8Options(nativePath))
        using (var int8Clone = int8.Clone())
        using (var fp8 = new MIGraphXQuantizeFp8Options(nativePath))
        using (var fp8Clone = fp8.Clone())
        {
            Assert.Equal("gpu", targetClone.Name);
            Assert.False(compileClone.OffloadCopy);
            Assert.True(compileClone.FastMath);
            Assert.True(compileClone.ExhaustiveTune);
            Assert.Equal("msgpack", fileClone.FileFormat);
            Assert.True(program.HasSameNativeContent(programClone));
            Assert.Equal(1, controls.TargetAssignCopied());
            Assert.Equal(1, controls.ProgramAssignCopied());

            names.Add("convolution");
            int8.AddOpName("dot");
            Assert.Empty(namesClone.Names);
            Assert.Empty(int8Clone.OpNames);

            var liveBeforeFailure = controls.M12LiveCount();
            controls.SetFailure("migraphx_quantize_op_names_assign_to", 4);
            var assignFailure = Assert.Throws<MIGraphXException>(() => names.Clone());
            Assert.Equal("migraphx_quantize_op_names_assign_to", assignFailure.Operation);
            Assert.Equal(liveBeforeFailure, controls.M12LiveCount());

            controls.SetNullOutput("migraphx_quantize_fp8_options_create");
            var nullCreate = Assert.Throws<MIGraphXException>(() => new MIGraphXQuantizeFp8Options(nativePath));
            Assert.Contains("success with null handle", nullCreate.Operation, StringComparison.Ordinal);
            Assert.Equal(liveBeforeFailure, controls.M12LiveCount());
        }

        AssertNoNativeLeaks(controls);
    }

    [Fact]
    public void GraphEditingAndContextViewsKeepTheirProgramAlive()
    {
        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();

        var program = new MIGraphXProgram(nativePath);
        var contextError = Assert.Throws<InvalidOperationException>(() => program.GetExperimentalContext());
        Assert.Contains("must be compiled", contextError.Message, StringComparison.Ordinal);
        using var target = new MIGraphXTarget(nativePath);
        using var compileOptions = new MIGraphXCompileOptions(nativePath);
        program.Compile(target, compileOptions);
        using (var main = program.GetMainModule())
        using (var branch = program.CreateModule("branch"))
        {
            var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
            using var literalValue = MIGraphXArgument.Create(nativePath, shape, new[] { 1f, 2f, 3f, 4f });
            using var parameter = main.AddParameter("input", shape);
            using var allocation = main.AddAllocation(shape);
            using var literal = main.AddLiteral(literalValue);
            using var inputs = new MIGraphXInstructions(nativePath, new[] { parameter, allocation, literal });
            using var inputClone = inputs.Clone();
            using var result = main.AddReturn(inputClone);
            using var modules = new MIGraphXModules(nativePath, new[] { main, branch });
            using var moduleClone = modules.Clone();

            Assert.Equal(3, inputs.Count);
            Assert.Equal(3, inputClone.Count);
            Assert.Equal(2, modules.Count);
            Assert.Equal(2, moduleClone.Count);
            main.Print();
            program.Print();
            program.Sort();
            Assert.Equal(1, controls.ProgramPrintCount());
            Assert.Equal(1, controls.ProgramSortCount());

            program.Dispose();
            Assert.Equal(1, controls.ProgramLiveCount());
            main.Print();
        }
        program.Dispose();

        var contextProgram = new MIGraphXProgram(nativePath);
        contextProgram.Compile(target, compileOptions);
        using (var context = contextProgram.GetExperimentalContext())
        {
            Assert.NotEqual(IntPtr.Zero, context.Queue);
            contextProgram.Dispose();
            context.Finish();
            Assert.Equal(1, controls.ContextFinishCount());
        }
        contextProgram.Dispose();
        target.Dispose();
        compileOptions.Dispose();

        AssertNoNativeLeaks(controls);
    }

    [Fact]
    public void TensorFlowAndQuantizationSurfacesForwardManagedState()
    {
        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();
        var tfFile = Path.Combine(Path.GetTempPath(), $"migraphx-m12-tf-{Guid.NewGuid():N}.pb");
        File.WriteAllBytes(tfFile, new byte[] { 1, 2, 3 });
        try
        {
            using (var tf = new MIGraphXTfOptions(nativePath))
            {
                tf.SetNhwc(true);
                tf.SetInputParameterShape("input", new long[] { 1, 4 });
                tf.SetDefaultDimValue(7);
                tf.SetOutputNames(new[] { "output", "aux" });
                using var tfClone = tf.Clone();
                Assert.True(tfClone.IsNhwc);
                Assert.Equal(7L, tfClone.DefaultDimValue.GetValueOrDefault());
                Assert.Equal(new[] { "output", "aux" }, tfClone.OutputNames);
                using var fromBuffer = MIGraphXProgram.ParseTfBuffer(new byte[] { 1, 2 }, tfClone);
                using var fromFile = MIGraphXProgram.ParseTfFile(tfFile, tf);
            }

            using (var program = new MIGraphXProgram(nativePath))
            using (var target = new MIGraphXTarget(nativePath))
            using (var compile = new MIGraphXCompileOptions(nativePath))
            using (var names = new MIGraphXQuantizeOpNames(nativePath))
            using (var int8 = new MIGraphXQuantizeInt8Options(nativePath))
            using (var fp8 = new MIGraphXQuantizeFp8Options(nativePath))
            using (var calibration = new MIGraphXParameterMap(nativePath))
            {
                names.Add("convolution");
                int8.AddOpName("dot");
                int8.AddCalibrationData(calibration);
                fp8.AddCalibrationData(calibration);

                program.QuantizeFp16(names);
                Assert.False(program.IsCompiled);
                Assert.Equal(16, controls.LastQuantization());

                program.QuantizeBf16();
                Assert.Equal(116, controls.LastQuantization());
                program.QuantizeInt8(target, int8);
                Assert.Equal(8, controls.LastQuantization());
                program.QuantizeFp8(target, fp8);
                Assert.Equal(18, controls.LastQuantization());

                program.Compile(target, compile);
                Assert.Throws<InvalidOperationException>(() => program.QuantizeFp16(names));
                Assert.Throws<InvalidOperationException>(() => program.QuantizeBf16());
                Assert.Throws<InvalidOperationException>(() => program.QuantizeInt8(target, int8));
                Assert.Throws<InvalidOperationException>(() => program.QuantizeFp8(target, fp8));
                Assert.True(program.IsCompiled);
                Assert.Equal(18, controls.LastQuantization());
            }
        }
        finally
        {
            File.Delete(tfFile);
        }

        AssertNoNativeLeaks(controls);
    }

    [Fact]
    public void CustomOpCloneReplaysCallbacksAndCreateFailuresReleaseState()
    {
        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();
        var state = new object();
        var callbackInvocations = 0;

        using (var operation = new MIGraphXExperimentalCustomOp(nativePath, "managed_test", state))
        {
            operation.SetCompute((_, _, _, _, _, _, _) => { callbackInvocations++; return MIGraphXStatus.Success; });
            operation.SetComputeShape((_, _, _, _, _) => { callbackInvocations++; return MIGraphXStatus.Success; });
            operation.SetOutputAlias((_, _, _, _, _, _) => { callbackInvocations++; return MIGraphXStatus.Success; });
            operation.SetRunsOnOffloadTarget((_, _, _, _) => { callbackInvocations++; return MIGraphXStatus.Success; });
            using var clone = operation.Clone();
            Assert.Same(state, clone.State);
            operation.Register();
            clone.Register();
            Assert.Equal(2, controls.CustomRegisterCount());
            Assert.Equal(0, controls.InvokeCustomCallbacks(operation.Owner.WithHandle(static handle => handle)));
            Assert.Equal(0, controls.InvokeCustomCallbacks(clone.Owner.WithHandle(static handle => handle)));
            Assert.Equal(8, callbackInvocations);
        }

        Assert.Equal(0, controls.M12LiveCount());
        controls.SetFailure("migraphx_experimental_custom_op_create", 4);
        var failedCreate = Assert.Throws<MIGraphXException>(
            () => new MIGraphXExperimentalCustomOp(nativePath, "failed", state));
        Assert.Equal("migraphx_experimental_custom_op_create", failedCreate.Operation);
        Assert.Equal(0, controls.M12LiveCount());

        controls.SetNullOutput("migraphx_experimental_custom_op_create");
        Assert.Throws<MIGraphXException>(
            () => new MIGraphXExperimentalCustomOp(nativePath, "null", state));

        AssertNoNativeLeaks(controls);
    }

    [Fact]
    public void OperationNoAttributeFactoryAndCloneOwnHandles()
    {
        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();

        using (var operation = MIGraphXOperation.Create(nativePath, "add"))
        using (var clone = operation.Clone())
        {
            Assert.Equal("add", operation.Name);
            Assert.Equal("add", clone.Name);
            Assert.Equal(2, controls.M12LiveCount());

            controls.SetFailure("migraphx_operation_assign_to", 4);
            var failure = Assert.Throws<MIGraphXException>(() => operation.Clone());
            Assert.Equal("migraphx_operation_assign_to", failure.Operation);
            Assert.Equal(2, controls.M12LiveCount());
        }

        controls.SetFailure("migraphx_operation_create", 4);
        var createFailure = Assert.Throws<MIGraphXException>(() => MIGraphXOperation.Create(nativePath, "mul"));
        Assert.Equal("migraphx_operation_create", createFailure.Operation);
        controls.SetNullOutput("migraphx_operation_create");
        Assert.Throws<MIGraphXException>(() => MIGraphXOperation.Create(nativePath, "sub"));
        AssertNoNativeLeaks(controls);
    }

    [Fact]
    public void OperationAttributeBuilderMaterializesCommonValuesAndRejectsUnsafeInput()
    {
        Assert.Equal("{dims: [1, 4]}", MIGraphXOperationAttributes.ForReshape(1, 4).Build());
        Assert.Equal("{permutation: [1, 0]}", MIGraphXOperationAttributes.ForTranspose(1, 0).Build());
        Assert.Equal("{axes: [0], starts: [0], ends: [1]}",
            MIGraphXOperationAttributes.ForSlice(new long[] { 0 }, new long[] { 0 }, new long[] { 1 }).Build());
        Assert.Equal("{out_lens: [1, 4]}", MIGraphXOperationAttributes.ForMultibroadcast(1, 4).Build());
        Assert.Equal("{axis: 1, k: 1, largest: true}", MIGraphXOperationAttributes.ForTopK(1, 1, true).Build());
        Assert.Throws<ArgumentException>(() =>
            MIGraphXOperationAttributes.ForSlice(new long[] { 0 }, new long[] { 0, 1 }, new long[] { 1 }));
        Assert.Throws<ArgumentNullException>(() => MIGraphXOperationAttributes.ForReshape(null!));

        var attributes = new MIGraphXOperationAttributes()
            .SetInt32("group", 2)
            .SetInt64Array("axes", new long[] { 1, 2, 3 })
            .SetSingle("value", 0.5f)
            .SetBoolean("largest", true)
            .SetBooleanArray("flags", new bool[] { true, false })
            .SetString("mode", "nearest")
            .SetString("pattern", "50%\\done\"ok")
            .SetNull("optional");

        Assert.Equal(
            "{group: 2, axes: [1, 2, 3], value: 0.5, largest: true, flags: [true, false], mode: \"nearest\", pattern: \"50%\\\\done\\\"ok\", optional: null}",
            attributes.Build());

        Assert.Throws<ArgumentException>(() => attributes.SetInt64("axes", 4));
        Assert.Throws<ArgumentException>(() => new MIGraphXOperationAttributes().SetString("bad-key", "value"));
        Assert.Throws<ArgumentException>(() => new MIGraphXOperationAttributes().SetString("bad", "a\0b"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MIGraphXOperationAttributes().SetSingle("bad", float.NaN));
        Assert.Throws<ArgumentNullException>(() => new MIGraphXOperationAttributes().SetBooleanArray("bad", null!));

        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();
        using (var operation = MIGraphXOperation.Create(nativePath, "reshape", attributes))
        {
            Assert.Equal("reshape", operation.Name);
            Assert.Equal(1, controls.M12LiveCount());
        }

        controls.SetFailure("migraphx_operation_create", 4);
        Assert.Throws<MIGraphXException>(() => MIGraphXOperation.Create(nativePath, "slice", attributes));
        controls.SetNullOutput("migraphx_operation_create");
        Assert.Throws<MIGraphXException>(() => MIGraphXOperation.Create(nativePath, "topk", attributes));
        AssertNoNativeLeaks(controls);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeferredNegativeBoundariesAndConcurrentDisposeRemainFailClosed()
    {
        Assert.Empty(typeof(MIGraphXOperation).GetConstructors());
        Assert.Empty(typeof(MIGraphXModule).GetConstructors());

        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();
        using var program = new MIGraphXProgram(nativePath);
        using var module = program.CreateModule("m12-concurrent-dispose");
        using var started = new System.Threading.ManualResetEventSlim(false);
        var worker = System.Threading.Tasks.Task.Run(() =>
        {
            started.Wait();
            for (var index = 0; index < 256; index++)
            {
                try { module.Print(); }
                catch (ObjectDisposedException) { break; }
            }
        });
        started.Set();
        module.Dispose();
        var completed = await System.Threading.Tasks.Task.WhenAny(worker, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(worker, completed);
        Assert.Throws<ObjectDisposedException>(() => module.Print());
        program.Dispose();

        var concurrentShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        using var argument = MIGraphXArgument.Create(nativePath, concurrentShape, new[] { 1f, 2f, 3f, 4f });
        await AssertConcurrentDisposeAsync(
            () => _ = argument.ToArray<float>(),
            argument.Dispose);

        using var compileOptions = new MIGraphXCompileOptions(nativePath);
        await AssertConcurrentDisposeAsync(
            () =>
            {
                using var clone = compileOptions.Clone();
            },
            compileOptions.Dispose);

        using var contextProgram = new MIGraphXProgram(nativePath);
        using var contextTarget = new MIGraphXTarget(nativePath);
        using var contextCompileOptions = new MIGraphXCompileOptions(nativePath);
        contextProgram.Compile(contextTarget, contextCompileOptions);
        using var context = contextProgram.GetExperimentalContext();
        await AssertConcurrentDisposeAsync(context.Finish, context.Dispose);

        using var customOp = new MIGraphXExperimentalCustomOp(nativePath, "m12-concurrent-custom-op", new object());
        await AssertConcurrentDisposeAsync(customOp.Register, customOp.Dispose);

        using var quantizeOptions = new MIGraphXQuantizeInt8Options(nativePath);
        await AssertConcurrentDisposeAsync(
            () => quantizeOptions.AddOpName("convolution"),
            quantizeOptions.Dispose);

        contextProgram.Dispose();
        contextTarget.Dispose();
        contextCompileOptions.Dispose();
        AssertNoNativeLeaks(controls);
    }

    private static async System.Threading.Tasks.Task AssertConcurrentDisposeAsync(Action access, Action dispose)
    {
        using var started = new System.Threading.ManualResetEventSlim(false);
        var worker = System.Threading.Tasks.Task.Run(() =>
        {
            started.Wait();
            for (var index = 0; index < 256; index++)
            {
                try { access(); }
                catch (ObjectDisposedException) { return; }
            }
        });
        started.Set();
        dispose();
        var completed = await System.Threading.Tasks.Task.WhenAny(worker, System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(worker, completed);
        await worker;
        Assert.Throws<ObjectDisposedException>(access);
        dispose();
    }

    private static void AssertNoNativeLeaks(FakeControls controls)
    {
        Assert.Equal(0, controls.TargetLiveCount());
        Assert.Equal(0, controls.ProgramLiveCount());
        Assert.Equal(0, controls.M2LiveCount());
        Assert.Equal(0, controls.M12LiveCount());
    }

    private static string FakePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln")))
            {
                var name = OperatingSystem.IsWindows() ? "migraphx_c.dll" : "libmigraphx_c.so";
                return Path.Combine(directory.FullName, "artifacts", "fake-native", "Release", name);
            }
        }
        throw new DirectoryNotFoundException("Could not locate the MIGraphXSharp repository root.");
    }

    private sealed class FakeControls : IDisposable
    {
        private readonly IntPtr library;
        private readonly Action reset;
        private readonly SetStringInt setFailure;
        private readonly SetString setNullOutput;
        private readonly GetInt targetAssignCopied;
        private readonly GetInt programAssignCopied;
        private readonly GetInt targetLiveCount;
        private readonly GetInt programLiveCount;
        private readonly GetInt m2LiveCount;
        private readonly GetInt m12LiveCount;
        private readonly GetInt lastQuantization;
        private readonly GetInt contextFinishCount;
        private readonly GetInt customRegisterCount;
        private readonly GetInt programPrintCount;
        private readonly GetInt programSortCount;
        private readonly CustomCallbackInvoker invokeCustomCallbacks;

        internal FakeControls(string path)
        {
            library = NativeLibrary.Load(path);
            reset = Get<Action>("fake_reset");
            setFailure = Get<SetStringInt>("fake_set_failure");
            setNullOutput = Get<SetString>("fake_set_null_output");
            targetAssignCopied = Get<GetInt>("fake_target_assign_copied");
            programAssignCopied = Get<GetInt>("fake_program_assign_copied");
            targetLiveCount = Get<GetInt>("fake_target_live_count");
            programLiveCount = Get<GetInt>("fake_program_live_count");
            m2LiveCount = Get<GetInt>("fake_m2_live_count");
            m12LiveCount = Get<GetInt>("fake_m12_live_count");
            lastQuantization = Get<GetInt>("fake_last_quantization");
            contextFinishCount = Get<GetInt>("fake_context_finish_count");
            customRegisterCount = Get<GetInt>("fake_custom_register_count");
            invokeCustomCallbacks = Get<CustomCallbackInvoker>("fake_invoke_custom_callbacks");
            programPrintCount = Get<GetInt>("fake_program_print_count");
            programSortCount = Get<GetInt>("fake_program_sort_count");
        }

        internal void Reset() => reset();
        internal void SetFailure(string entryPoint, int status) => setFailure(entryPoint, status);
        internal void SetNullOutput(string entryPoint) => setNullOutput(entryPoint);
        internal int TargetAssignCopied() => targetAssignCopied();
        internal int ProgramAssignCopied() => programAssignCopied();
        internal int TargetLiveCount() => targetLiveCount();
        internal int ProgramLiveCount() => programLiveCount();
        internal int M2LiveCount() => m2LiveCount();
        internal int M12LiveCount() => m12LiveCount();
        internal int LastQuantization() => lastQuantization();
        internal int ContextFinishCount() => contextFinishCount();
        internal int CustomRegisterCount() => customRegisterCount();
        internal int InvokeCustomCallbacks(IntPtr operation) => invokeCustomCallbacks(operation);
        internal int ProgramPrintCount() => programPrintCount();
        internal int ProgramSortCount() => programSortCount();
        public void Dispose() => NativeLibrary.Free(library);
        private T Get<T>(string name) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetInt();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CustomCallbackInvoker(IntPtr operation);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetString([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetStringInt([MarshalAs(UnmanagedType.LPUTF8Str)] string value, int status);
    }
}
