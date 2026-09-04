using System.Runtime.InteropServices;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M4ManagedObjectTests
{
    [Fact]
    public void ShapeMetadataIsImmutableMappedAndOverflowChecked()
    {
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 2, 3, 4 });

        Assert.Equal(MIGraphXShapeDataType.Float32, shape.DataType);
        Assert.Equal(new long[] { 2, 3, 4 }, shape.Lengths);
        Assert.Equal(new long[] { 12, 4, 1 }, shape.Strides);
        Assert.Equal(3, shape.Rank);
        Assert.Equal(24, shape.ElementCount);
        Assert.Equal(96, shape.ByteCount);
        Assert.True(shape.IsStandard);
        Assert.True(shape.IsPacked);

        var scalar = new MIGraphXShape(MIGraphXShapeDataType.Int64, Array.Empty<long>());
        Assert.Equal(0, scalar.Rank);
        Assert.Equal(1, scalar.ElementCount);
        Assert.Equal(8, scalar.ByteCount);

        Assert.Throws<ArgumentOutOfRangeException>(() => new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MIGraphXShape((MIGraphXShapeDataType)999, new long[] { 1 }));
        Assert.Throws<OverflowException>(() => new MIGraphXShape(MIGraphXShapeDataType.Float64, new long[] { long.MaxValue, 2 }));
    }

    [Fact]
    public void InferenceOptionsForwardValuesValidatePathsAndPreserveEntryPointFailures()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();

        using (var compile = new MIGraphXCompileOptions(path, offloadCopy: false, fastMath: true, exhaustiveTune: true))
        {
            Assert.False(compile.OffloadCopy);
            Assert.True(compile.FastMath);
            Assert.True(compile.ExhaustiveTune);
            Assert.Equal(1, controls.LastFastMath());
            Assert.Equal(1, controls.LastExhaustiveTune());
        }

        using (var options = new MIGraphXOnnxOptions(path))
        {
            var externalData = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "migraphx-external-data-中文"));
            options.SetDefaultLoopIterations(17);
            options.SetLimitLoopIterations(2048);
            options.SetExternalDataPath(externalData);

            Assert.Equal(17, controls.LastDefaultLoopIterations());
            Assert.Equal(2048, controls.LastLimitLoopIterations());
            Assert.Equal(externalData, controls.LastExternalDataPath());
            Assert.Throws<ArgumentOutOfRangeException>(() => options.SetDefaultLoopIterations(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => options.SetLimitLoopIterations(-1));
            Assert.Throws<ArgumentException>(() => options.SetExternalDataPath("relative"));
            Assert.Throws<ArgumentException>(() => options.SetExternalDataPath("\ud800"));

            foreach (var entryPoint in new[]
            {
                "migraphx_onnx_options_set_default_loop_iterations",
                "migraphx_onnx_options_set_limit_loop_iterations",
                "migraphx_onnx_options_set_external_data_path",
            })
            {
                controls.SetFailure(entryPoint, (int)MIGraphXStatus.UnknownError);
                var error = entryPoint.EndsWith("default_loop_iterations", StringComparison.Ordinal)
                    ? Assert.Throws<MIGraphXException>(() => options.SetDefaultLoopIterations(1))
                    : entryPoint.EndsWith("limit_loop_iterations", StringComparison.Ordinal)
                        ? Assert.Throws<MIGraphXException>(() => options.SetLimitLoopIterations(1))
                        : Assert.Throws<MIGraphXException>(() => options.SetExternalDataPath(Path.GetTempPath()));
                Assert.Equal(entryPoint, error.Operation);
            }
        }

        foreach (var entryPoint in new[]
        {
            "migraphx_compile_options_set_fast_math",
            "migraphx_compile_options_set_exhaustive_tune_flag",
        })
        {
            controls.SetFailure(entryPoint, (int)MIGraphXStatus.UnknownError);
            Assert.Equal(entryPoint, Assert.Throws<MIGraphXException>(() =>
                new MIGraphXCompileOptions(path, offloadCopy: true, fastMath: true, exhaustiveTune: true)).Operation);
            AssertNoLeaks(controls);
        }
    }

    [Fact]
    public void ManagedObjectsComposeParseCompileTypedRunAndDeterministicDispose()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();
        var model = new byte[] { 0x08, 0x01, 0x12, 0x02 };
        var input = new[] { 1f, -2f, 3.5f, 4f };

        using (var onnxOptions = new MIGraphXOnnxOptions(path))
        using (var program = MIGraphXProgram.ParseOnnxBuffer(model, onnxOptions))
        using (var target = new MIGraphXTarget(path))
        using (var compileOptions = new MIGraphXCompileOptions(path))
        {
            var parameterShapes = program.GetParameterShapes();
            Assert.Equal(new[] { "input" }, parameterShapes.Keys);
            Assert.Equal(new long[] { 1, 4 }, parameterShapes["input"].Lengths);
            Assert.False(program.IsCompiled);

            program.Compile(target, compileOptions);
            Assert.True(program.IsCompiled);
            Assert.Single(program.GetOutputShapes());

            using (var argument = MIGraphXArgument.Create(path, parameterShapes["input"], input))
            using (var parameters = new MIGraphXParameterMap(path))
            {
                parameters.Add("input", argument);
                input[0] = 99f;
                argument.Dispose();

                using (var outputs = program.Run(parameters))
                {
                    Assert.Single(outputs);
                    Assert.Equal(new[] { 1f, -2f, 3.5f, 4f }, outputs[0].ToArray<float>());
                    Assert.Throws<ArgumentException>(() => outputs[0].ToArray<int>());
                }

                controls.SetShapeMode(6);
                using (var outputs = program.Run(parameters))
                {
                    Assert.Equal(2, outputs.Count);
                    Assert.Equal(new[] { 1f, -2f, 3.5f, 4f }, outputs[1].ToArray<float>());
                }
                controls.SetShapeMode(0);
            }

            compileOptions.Dispose();
            target.Dispose();
            program.Dispose();
            onnxOptions.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _ = program.IsCompiled);
        }

        AssertNoLeaks(controls);
        Assert.Equal(2, controls.RunCount());
    }

    [Fact]
    public void TypedArgumentsRoundTripEveryMappedScalar()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();

        AssertRoundTrip(path, MIGraphXShapeDataType.Float32, new[] { 1.25f, -2f });
        AssertRoundTrip(path, MIGraphXShapeDataType.Float64, new[] { 1.25, -2.0 });
        AssertRoundTrip(path, MIGraphXShapeDataType.UInt8, new byte[] { 1, 255 });
        AssertRoundTrip(path, MIGraphXShapeDataType.Int8, new sbyte[] { -1, 127 });
        AssertRoundTrip(path, MIGraphXShapeDataType.UInt16, new ushort[] { 1, 65535 });
        AssertRoundTrip(path, MIGraphXShapeDataType.Int16, new short[] { -1, 32767 });
        AssertRoundTrip(path, MIGraphXShapeDataType.Int32, new[] { -1, int.MaxValue });
        AssertRoundTrip(path, MIGraphXShapeDataType.Int64, new[] { -1L, long.MaxValue });
        AssertRoundTrip(path, MIGraphXShapeDataType.UInt32, new[] { 1U, uint.MaxValue });
        AssertRoundTrip(path, MIGraphXShapeDataType.UInt64, new[] { 1UL, ulong.MaxValue });

        AssertNoLeaks(controls);
    }

    [Fact]
    public void SnapshotsHandleMultipleItemsAndRejectMalformedNativeCollections()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();
        using var options = new MIGraphXOnnxOptions(path);
        using var program = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options);

        controls.SetShapeMode(4);
        var parameters = program.GetParameterShapes();
        Assert.Equal(new[] { "input", "second" }, parameters.Keys);

        using (var target = new MIGraphXTarget(path))
        using (var compileOptions = new MIGraphXCompileOptions(path))
        using (var first = MIGraphXArgument.Create(path, parameters["input"], new[] { 1f, 2f, 3f, 4f }))
        using (var second = MIGraphXArgument.Create(path, parameters["second"], new[] { 9f, 8f, 7f, 6f }))
        {
            program.Compile(target, compileOptions);
            using (var missing = new MIGraphXParameterMap(path))
            {
                missing.Add("input", first);
                Assert.Throws<ArgumentException>(() => program.Run(missing));
            }

            using (var complete = new MIGraphXParameterMap(path))
            {
                complete.Add("second", second);
                complete.Add("input", first);
                first.Dispose();
                second.Dispose();
                using (var outputs = program.Run(complete))
                {
                    Assert.Equal(new[] { 9f, 8f, 7f, 6f }, outputs[0].ToArray<float>());
                }
                controls.SetSkipOutput("migraphx_arguments_size");
                Assert.Throws<MIGraphXException>(() => program.Run(complete));
                Assert.Equal(2, controls.LastParameterCount());
            }
        }

        controls.SetShapeMode(5);
        Assert.Equal(2, program.GetOutputShapes().Count);

        controls.SetShapeMode(2);
        var nonStandard = Assert.Single(program.GetOutputShapes());
        Assert.False(nonStandard.IsStandard);
        Assert.True(nonStandard.IsPacked);
        Assert.Throws<NotSupportedException>(() => MIGraphXArgument.Create(path, nonStandard, new float[4]));

        foreach (var testCase in new[]
        {
            (Mode: 1, Exception: typeof(NotSupportedException)),
            (Mode: 7, Exception: typeof(MIGraphXException)),
            (Mode: 8, Exception: typeof(MIGraphXException)),
            (Mode: 10, Exception: typeof(InvalidOperationException)),
            (Mode: 11, Exception: typeof(MIGraphXException)),
            (Mode: 12, Exception: typeof(MIGraphXException)),
            (Mode: 13, Exception: typeof(OverflowException)),
            (Mode: 14, Exception: typeof(InvalidOperationException)),
            (Mode: 17, Exception: typeof(MIGraphXException)),
        })
        {
            controls.SetShapeMode(testCase.Mode);
            var error = Record.Exception(() =>
            {
                if (testCase.Mode == 8) { program.GetOutputShapes(); }
                else { program.GetParameterShapes(); }
            });
            Assert.NotNull(error);
            Assert.IsType(testCase.Exception, error);
            if (testCase.Mode == 11)
            {
                var duplicate = Assert.IsType<MIGraphXException>(error);
                Assert.Contains("migraphx_program_parameter_shapes_names", duplicate.Operation, StringComparison.Ordinal);
                Assert.Contains("duplicate parameter name 'input'", duplicate.Operation, StringComparison.Ordinal);
            }
        }

        controls.SetShapeMode(18);
        var emptyParameters = program.GetParameterShapes();
        Assert.Empty(emptyParameters);
        controls.SetShapeMode(0);
        foreach (var borrowedEntryPoint in new[]
        {
            "migraphx_program_parameter_shapes_get",
            "migraphx_shapes_get",
        })
        {
            controls.SetNullOutput(borrowedEntryPoint);
            var nullBorrowed = Assert.Throws<MIGraphXException>(() =>
            {
                if (borrowedEntryPoint == "migraphx_shapes_get") { program.GetOutputShapes(); }
                else { program.GetParameterShapes(); }
            });
            Assert.Contains(borrowedEntryPoint, nullBorrowed.Operation, StringComparison.Ordinal);
            Assert.Contains("success with null borrowed handle", nullBorrowed.Operation, StringComparison.Ordinal);
        }

        controls.SetSkipOutput("migraphx_program_parameter_shapes_size");
        Assert.Throws<MIGraphXException>(() => program.GetParameterShapes());
        controls.SetSkipOutput("migraphx_shapes_size");
        Assert.Throws<MIGraphXException>(() => program.GetOutputShapes());
        foreach (var entryPoint in new[]
        {
            "migraphx_shape_type",
            "migraphx_shape_lengths",
            "migraphx_shape_strides",
            "migraphx_shape_elements",
            "migraphx_shape_bytes",
        })
        {
            controls.SetSkipOutput(entryPoint);
            var unwritten = Assert.Throws<MIGraphXException>(() => program.GetOutputShapes());
            Assert.Contains($"{entryPoint} (success without writing", unwritten.Operation, StringComparison.Ordinal);
        }

        controls.SetShapeType(5);
        var doubleShape = Assert.Single(program.GetOutputShapes());
        Assert.Equal(MIGraphXShapeDataType.Float64, doubleShape.DataType);
        Assert.Equal(32, doubleShape.ByteCount);

        controls.SetShapeType(0);
        Assert.Throws<NotSupportedException>(() => program.GetOutputShapes());
        controls.SetShapeType(-1);
    }

    [Fact]
    public void EntryPointFailuresAndNullOwnedHandlesCleanUp()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();

        controls.SetFailure("migraphx_target_create", (int)MIGraphXStatus.UnknownError);
        Assert.Equal("migraphx_target_create", Assert.Throws<MIGraphXException>(() => new MIGraphXTarget(path)).Operation);
        AssertNoLeaks(controls);

        controls.SetNullOutput("migraphx_onnx_options_create");
        Assert.Contains("success with null handle", Assert.Throws<MIGraphXException>(() => new MIGraphXOnnxOptions(path)).Operation, StringComparison.Ordinal);
        AssertNoLeaks(controls);

        using var options = new MIGraphXOnnxOptions(path);
        controls.SetFailure("migraphx_parse_onnx_buffer", (int)MIGraphXStatus.UnknownError);
        Assert.Equal("migraphx_parse_onnx_buffer", Assert.Throws<MIGraphXException>(() => MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options)).Operation);
        AssertNoLeaksExcept(controls, 1);

        using var program = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options);
        foreach (var entryPoint in new[]
        {
            "migraphx_program_get_parameter_shapes",
            "migraphx_program_parameter_shapes_size",
            "migraphx_program_parameter_shapes_names",
            "migraphx_program_parameter_shapes_get",
            "migraphx_shape_type",
            "migraphx_shape_lengths",
            "migraphx_shape_strides",
            "migraphx_shape_elements",
            "migraphx_shape_bytes",
            "migraphx_shape_standard",
            "migraphx_shape_dynamic",
        })
        {
            controls.SetFailure(entryPoint, (int)MIGraphXStatus.UnknownError);
            var error = Assert.Throws<MIGraphXException>(() => program.GetParameterShapes());
            Assert.Equal(entryPoint, error.Operation);
            AssertNoLeaksExcept(controls, 1);
        }

        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        controls.SetFailure("migraphx_shape_create", (int)MIGraphXStatus.UnknownError);
        Assert.Equal("migraphx_shape_create", Assert.Throws<MIGraphXException>(() => MIGraphXArgument.Create(path, shape, new float[4])).Operation);
        AssertNoLeaksExcept(controls, 1);

        controls.SetNullOutput("migraphx_argument_create");
        Assert.Contains("success with null handle", Assert.Throws<MIGraphXException>(() => MIGraphXArgument.Create(path, shape, new float[4])).Operation, StringComparison.Ordinal);
        AssertNoLeaksExcept(controls, 1);
    }

    [Fact]
    public void CompileMapOutputAndRunEntryPointFailuresReleasePartialState()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);

        foreach (var creation in new (string EntryPoint, Action Action)[]
        {
            ("migraphx_onnx_options_create", () => new MIGraphXOnnxOptions(path)),
            ("migraphx_compile_options_create", () => new MIGraphXCompileOptions(path)),
            ("migraphx_compile_options_set_offload_copy", () => new MIGraphXCompileOptions(path)),
            ("migraphx_program_create", () => new MIGraphXProgram(path)),
            ("migraphx_program_parameters_create", () => new MIGraphXParameterMap(path)),
            ("migraphx_argument_create", () => MIGraphXArgument.Create(path, new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 }), new float[4])),
        })
        {
            controls.Reset();
            controls.SetFailure(creation.EntryPoint, (int)MIGraphXStatus.UnknownError);
            var error = Assert.Throws<MIGraphXException>(creation.Action);
            Assert.Equal(creation.EntryPoint, error.Operation);
            AssertNoLeaks(controls);
        }

        controls.Reset();
        using var parseOptions = new MIGraphXOnnxOptions(path);
        using var program = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, parseOptions);
        using var target = new MIGraphXTarget(path);
        using var compileOptions = new MIGraphXCompileOptions(path);

        controls.SetFailure("migraphx_program_compile", (int)MIGraphXStatus.UnknownError);
        Assert.Equal("migraphx_program_compile", Assert.Throws<MIGraphXException>(() => program.Compile(target, compileOptions)).Operation);
        Assert.False(program.IsCompiled);
        program.Compile(target, compileOptions);

        var shape = program.GetParameterShapes()["input"];
        using var input = MIGraphXArgument.Create(path, shape, new[] { 1f, 2f, 3f, 4f });
        using var parameters = new MIGraphXParameterMap(path);
        controls.SetFailure("migraphx_program_parameters_add", (int)MIGraphXStatus.UnknownError);
        Assert.Equal("migraphx_program_parameters_add", Assert.Throws<MIGraphXException>(() => parameters.Add("input", input)).Operation);
        Assert.Equal(0, parameters.Count);
        parameters.Add("input", input);

        foreach (var entryPoint in new[]
        {
            "migraphx_program_get_output_shapes",
            "migraphx_shapes_size",
            "migraphx_shapes_get",
        })
        {
            controls.SetFailure(entryPoint, (int)MIGraphXStatus.UnknownError);
            Assert.Equal(entryPoint, Assert.Throws<MIGraphXException>(() => program.GetOutputShapes()).Operation);
        }

        foreach (var entryPoint in new[]
        {
            "migraphx_program_run",
            "migraphx_arguments_size",
            "migraphx_arguments_get",
            "migraphx_argument_shape",
            "migraphx_argument_buffer",
            "migraphx_shape_create",
            "migraphx_argument_create",
        })
        {
            controls.SetFailure(entryPoint, (int)MIGraphXStatus.UnknownError);
            Assert.Equal(entryPoint, Assert.Throws<MIGraphXException>(() => program.Run(parameters)).Operation);
        }

        foreach (var nullOutput in new[]
        {
            (EntryPoint: "migraphx_arguments_get", Reason: "success with null borrowed handle"),
            (EntryPoint: "migraphx_argument_shape", Reason: "success with null borrowed handle"),
            (EntryPoint: "migraphx_argument_buffer", Reason: "success with null buffer"),
        })
        {
            controls.SetNullOutput(nullOutput.EntryPoint);
            var error = Assert.Throws<MIGraphXException>(() => program.Run(parameters));
            Assert.Contains(nullOutput.EntryPoint, error.Operation, StringComparison.Ordinal);
            Assert.Contains(nullOutput.Reason, error.Operation, StringComparison.Ordinal);
        }

        controls.SetFailure("migraphx_arguments_destroy", (int)MIGraphXStatus.UnknownError);
        using (var outputs = program.Run(parameters))
        {
            Assert.Single(outputs);
        }

        parameters.Dispose();
        input.Dispose();
        compileOptions.Dispose();
        target.Dispose();
        program.Dispose();
        parseOptions.Dispose();
        AssertNoLeaks(controls);
    }

    [Fact]
    public void DestroyStatusFailuresStillReleaseEveryOwnedResource()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();

        var parseOptions = new MIGraphXOnnxOptions(path);
        var program = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, parseOptions);
        var target = new MIGraphXTarget(path);
        var compileOptions = new MIGraphXCompileOptions(path);
        program.Compile(target, compileOptions);

        controls.SetFailure("migraphx_program_parameter_shapes_destroy", (int)MIGraphXStatus.UnknownError);
        var shape = program.GetParameterShapes()["input"];

        controls.SetFailure("migraphx_shape_destroy", (int)MIGraphXStatus.UnknownError);
        var input = MIGraphXArgument.Create(path, shape, new[] { 1f, 2f, 3f, 4f });
        var parameters = new MIGraphXParameterMap(path);
        parameters.Add("input", input);

        controls.SetFailure("migraphx_shapes_destroy", (int)MIGraphXStatus.UnknownError);
        Assert.Single(program.GetOutputShapes());

        controls.SetFailure("migraphx_arguments_destroy", (int)MIGraphXStatus.UnknownError);
        var outputs = program.Run(parameters);
        Assert.Single(outputs);

        controls.SetFailure("migraphx_argument_destroy", (int)MIGraphXStatus.UnknownError);
        outputs.Dispose();
        controls.SetFailure("migraphx_program_parameters_destroy", (int)MIGraphXStatus.UnknownError);
        parameters.Dispose();
        controls.SetFailure("migraphx_argument_destroy", (int)MIGraphXStatus.UnknownError);
        input.Dispose();
        controls.SetFailure("migraphx_compile_options_destroy", (int)MIGraphXStatus.UnknownError);
        compileOptions.Dispose();
        controls.SetFailure("migraphx_target_destroy", (int)MIGraphXStatus.UnknownError);
        target.Dispose();
        controls.SetFailure("migraphx_program_destroy", (int)MIGraphXStatus.UnknownError);
        program.Dispose();
        controls.SetFailure("migraphx_onnx_options_destroy", (int)MIGraphXStatus.UnknownError);
        parseOptions.Dispose();

        AssertNoLeaks(controls);
    }

    private static void AssertNoLeaks(FakeControls controls)
    {
        Assert.Equal(0, controls.M2LiveCount());
        Assert.Equal(0, controls.ProgramLiveCount());
        Assert.Equal(0, controls.TargetLiveCount());
    }

    private static void AssertRoundTrip<T>(string path, MIGraphXShapeDataType dataType, T[] values)
        where T : unmanaged
    {
        var shape = new MIGraphXShape(dataType, new long[] { values.LongLength });
        using var argument = MIGraphXArgument.Create(path, shape, values);
        Assert.Equal(values, argument.ToArray<T>());
    }

    private static void AssertNoLeaksExcept(FakeControls controls, int expectedM2)
    {
        Assert.Equal(expectedM2, controls.M2LiveCount());
        Assert.Equal(0, controls.TargetLiveCount());
    }

    private static string FakePath()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "artifacts", "fake-native", "Release");
        return Path.Combine(directory, OperatingSystem.IsWindows() ? "migraphx_c.dll" : "libmigraphx_c.so");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln"))) { return directory.FullName; }
        }
        throw new DirectoryNotFoundException("Could not locate the MIGraphXSharp repository root.");
    }

    private sealed class FakeControls : IDisposable
    {
        private readonly IntPtr library;
        private readonly ResetDelegate reset;
        private readonly SetStringIntDelegate setFailure;
        private readonly SetStringDelegate setNullOutput;
        private readonly SetStringDelegate setSkipOutput;
        private readonly SetIntDelegate setShapeMode;
        private readonly SetIntDelegate setShapeType;
        private readonly GetIntDelegate m2LiveCount;
        private readonly GetIntDelegate programLiveCount;
        private readonly GetIntDelegate targetLiveCount;
        private readonly GetIntDelegate runCount;
        private readonly GetIntDelegate lastParameterCount;
        private readonly GetLongDelegate lastDefaultLoopIterations;
        private readonly GetLongDelegate lastLimitLoopIterations;
        private readonly GetIntDelegate lastFastMath;
        private readonly GetIntDelegate lastExhaustiveTune;
        private readonly GetPointerDelegate lastExternalDataPath;

        internal FakeControls(string path)
        {
            library = NativeLibrary.Load(path);
            reset = Get<ResetDelegate>("fake_reset");
            setFailure = Get<SetStringIntDelegate>("fake_set_failure");
            setNullOutput = Get<SetStringDelegate>("fake_set_null_output");
            setSkipOutput = Get<SetStringDelegate>("fake_set_skip_output");
            setShapeMode = Get<SetIntDelegate>("fake_set_shape_mode");
            setShapeType = Get<SetIntDelegate>("fake_set_shape_type");
            m2LiveCount = Get<GetIntDelegate>("fake_m2_live_count");
            programLiveCount = Get<GetIntDelegate>("fake_program_live_count");
            targetLiveCount = Get<GetIntDelegate>("fake_target_live_count");
            runCount = Get<GetIntDelegate>("fake_run_count");
            lastParameterCount = Get<GetIntDelegate>("fake_last_parameter_count");
            lastDefaultLoopIterations = Get<GetLongDelegate>("fake_last_default_loop_iterations");
            lastLimitLoopIterations = Get<GetLongDelegate>("fake_last_limit_loop_iterations");
            lastFastMath = Get<GetIntDelegate>("fake_last_fast_math");
            lastExhaustiveTune = Get<GetIntDelegate>("fake_last_exhaustive_tune");
            lastExternalDataPath = Get<GetPointerDelegate>("fake_last_external_data_path");
        }

        internal void Reset() => reset();
        internal void SetFailure(string entryPoint, int status) => setFailure(entryPoint, status);
        internal void SetNullOutput(string entryPoint) => setNullOutput(entryPoint);
        internal void SetSkipOutput(string entryPoint) => setSkipOutput(entryPoint);
        internal void SetShapeMode(int value) => setShapeMode(value);
        internal void SetShapeType(int value) => setShapeType(value);
        internal int M2LiveCount() => m2LiveCount();
        internal int ProgramLiveCount() => programLiveCount();
        internal int TargetLiveCount() => targetLiveCount();
        internal int RunCount() => runCount();
        internal int LastParameterCount() => lastParameterCount();
        internal long LastDefaultLoopIterations() => lastDefaultLoopIterations();
        internal long LastLimitLoopIterations() => lastLimitLoopIterations();
        internal int LastFastMath() => lastFastMath();
        internal int LastExhaustiveTune() => lastExhaustiveTune();
        internal string LastExternalDataPath() => Marshal.PtrToStringUTF8(lastExternalDataPath())!;

        public void Dispose() => NativeLibrary.Free(library);

        private T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ResetDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetIntDelegate(int value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate void SetStringDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate void SetStringIntDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string value, int status);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetIntDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long GetLongDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetPointerDelegate();
    }
}
