using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M10CapabilityEqualityTests
{
    [Fact]
    public void OnnxRegistryCopiesStableUtf8AndFailsClosedForEveryInjectedFault()
    {
        var nativePath = GetFakeNativePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();

        var names = MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath);
        Assert.Equal(new[] { "Add", "\u52a0", "Relu" }, names);
        Assert.Equal(0, controls.GetNameAtIndex(2));
        Assert.Equal(1, controls.GetNameAtIndex(3));

        controls.SetRegistryMode(1);
        Assert.Empty(MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath));

        controls.SetRegistryMode(2);
        var overflow = Assert.Throws<OverflowException>(() => MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath));
        Assert.Contains("migraphx_get_onnx_operators_size", overflow.Message, StringComparison.Ordinal);

        controls.SetRegistryMode(3);
        var nullName = Assert.Throws<MIGraphXException>(() => MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath));
        Assert.Equal(4, nullName.StatusCode);
        Assert.Contains("migraphx_get_onnx_operator_name_at_index (index 1)", nullName.Operation, StringComparison.Ordinal);

        controls.SetRegistryMode(4);
        var invalidUtf8 = Assert.Throws<InvalidDataException>(() => MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath));
        Assert.Contains("migraphx_get_onnx_operator_name_at_index (index 1)", invalidUtf8.Message, StringComparison.Ordinal);

        controls.SetRegistryMode(5);
        var midFailure = Assert.Throws<MIGraphXException>(() => MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath));
        Assert.Equal(4, midFailure.StatusCode);
        Assert.Equal("migraphx_get_onnx_operator_name_at_index (index 1)", midFailure.Operation);

        controls.SetRegistryMode(6);
        var drift = Assert.Throws<InvalidOperationException>(() => MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath));
        Assert.Contains("migraphx_get_onnx_operators_size changed from 3 to 4", drift.Message, StringComparison.Ordinal);

        controls.SetRegistryMode(0);
        controls.SetFailure("migraphx_get_onnx_operators_size", 3);
        var sizeFailure = Assert.Throws<MIGraphXException>(() => MIGraphXOnnxWorkflow.GetRegisteredOperators(nativePath));
        Assert.Equal(3, sizeFailure.StatusCode);
        Assert.Equal("migraphx_get_onnx_operators_size", sizeFailure.Operation);

        var alternatePath = Path.Combine(Path.GetTempPath(), $"migraphx-m10-root-{Guid.NewGuid():N}{Path.GetExtension(nativePath)}");
        try
        {
            File.Copy(nativePath, alternatePath);
            Assert.Throws<MIGraphXNativeLoadException>(() => MIGraphXOnnxWorkflow.GetRegisteredOperators(alternatePath));
        }
        finally
        {
            File.Delete(alternatePath);
        }

        Assert.Equal(0, controls.M2LiveCount());
        Assert.Equal(0, controls.ProgramLiveCount());
    }

    [Fact]
    public async Task ArgumentContentComparisonHandlesIndependentValuesFailuresConcurrencyAndDispose()
    {
        var nativePath = GetFakeNativePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });

        using (var left = MIGraphXArgument.Create(nativePath, shape, new[] { 1f, 2f, 3f, 4f }))
        using (var same = MIGraphXArgument.Create(nativePath, shape, new[] { 1f, 2f, 3f, 4f }))
        using (var different = MIGraphXArgument.Create(nativePath, shape, new[] { 1f, 2f, 3f, 5f }))
        using (var differentShape = MIGraphXArgument.Create(
            nativePath,
            new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 2, 2 }),
            new[] { 1f, 2f, 3f, 4f }))
        {
            Assert.True(left.HasSameNativeContent(left));
            Assert.True(left.HasSameNativeContent(same));
            Assert.False(left.HasSameNativeContent(different));
            Assert.False(left.HasSameNativeContent(differentShape));
            Assert.Throws<ArgumentNullException>(() => left.HasSameNativeContent(null!));

            Parallel.Invoke(
                () => Repeat(100, () => Assert.True(left.HasSameNativeContent(same))),
                () => Repeat(100, () => Assert.True(same.HasSameNativeContent(left))));

            controls.SetFailure("migraphx_argument_equal", 3);
            var failure = Assert.Throws<MIGraphXException>(() => left.HasSameNativeContent(same));
            Assert.Equal(3, failure.StatusCode);
            Assert.Equal("migraphx_argument_equal", failure.Operation);

            controls.SetInvalidBool("migraphx_argument_equal");
            var invalid = Assert.Throws<MIGraphXException>(() => left.HasSameNativeContent(same));
            Assert.Contains("success with invalid C bool 2", invalid.Operation, StringComparison.Ordinal);

            using (var disposing = MIGraphXArgument.Create(nativePath, shape, new[] { 1f, 2f, 3f, 4f }))
            {
                var enteredBefore = controls.EqualityEnterCount();
                controls.SetEqualityWait(1);
                var comparison = Task.Run(() => left.HasSameNativeContent(disposing));
                Task? disposal = null;
                try
                {
                    Assert.True(SpinWait.SpinUntil(() => controls.EqualityEnterCount() > enteredBefore, TimeSpan.FromSeconds(5)));
                    disposal = Task.Run(disposing.Dispose);
                    await Task.Delay(100);
                    Assert.False(disposal.IsCompleted);
                }
                finally
                {
                    controls.SetEqualityWait(0);
                }
                Assert.True(await comparison.WaitAsync(TimeSpan.FromSeconds(5)));
                await disposal!.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Throws<ObjectDisposedException>(() => left.HasSameNativeContent(disposing));
            }

            same.Dispose();
            Assert.Throws<ObjectDisposedException>(() => left.HasSameNativeContent(same));
        }

        Assert.Equal(0, controls.M2LiveCount());
        Assert.Equal(0, controls.ProgramLiveCount());
    }

    [Fact]
    public async Task ProgramContentComparisonUsesOrderedLocksAndKeepsHandlesAliveAgainstDispose()
    {
        var nativePath = GetFakeNativePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();

        using (var left = new MIGraphXProgram(nativePath))
        using (var right = new MIGraphXProgram(nativePath))
        using (var target = new MIGraphXTarget(nativePath))
        using (var options = new MIGraphXCompileOptions(nativePath, offloadCopy: true))
        {
            Assert.True(left.HasSameNativeContent(left));
            Assert.True(left.HasSameNativeContent(right));
            Assert.Throws<ArgumentNullException>(() => left.HasSameNativeContent(null!));
            left.Compile(target, options);
            Assert.False(left.HasSameNativeContent(right));
            right.Compile(target, options);
            Assert.True(left.HasSameNativeContent(right));

            using var firstParseOptions = new MIGraphXOnnxOptions(nativePath);
            using var secondParseOptions = new MIGraphXOnnxOptions(nativePath);
            using var differentParseOptions = new MIGraphXOnnxOptions(nativePath);
            firstParseOptions.SetInputParameterShape("input", new long[] { 1, 4 });
            secondParseOptions.SetInputParameterShape("input", new long[] { 1, 4 });
            differentParseOptions.SetInputParameterShape("input", new long[] { 2, 2 });
            using var firstParsed = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, firstParseOptions);
            using var secondParsed = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, secondParseOptions);
            using var differentlyParsed = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, differentParseOptions);
            Assert.False(left.HasSameNativeContent(firstParsed));
            Assert.True(firstParsed.HasSameNativeContent(secondParsed));
            Assert.False(firstParsed.HasSameNativeContent(differentlyParsed));
            firstParsed.Compile(target, options);
            Assert.False(firstParsed.HasSameNativeContent(secondParsed));
            secondParsed.Compile(target, options);
            Assert.True(firstParsed.HasSameNativeContent(secondParsed));

            Parallel.Invoke(
                () => Repeat(100, () => Assert.True(left.HasSameNativeContent(right))),
                () => Repeat(100, () => Assert.True(right.HasSameNativeContent(left))));

            controls.SetFailure("migraphx_program_equal", 4);
            var failure = Assert.Throws<MIGraphXException>(() => left.HasSameNativeContent(right));
            Assert.Equal(4, failure.StatusCode);
            Assert.Equal("migraphx_program_equal", failure.Operation);

            controls.SetInvalidBool("migraphx_program_equal");
            var invalid = Assert.Throws<MIGraphXException>(() => left.HasSameNativeContent(right));
            Assert.Contains("success with invalid C bool 2", invalid.Operation, StringComparison.Ordinal);

            var enteredBefore = controls.EqualityEnterCount();
            controls.SetEqualityWait(1);
            var comparison = Task.Run(() => left.HasSameNativeContent(right));
            Task? disposal = null;
            try
            {
                Assert.True(SpinWait.SpinUntil(() => controls.EqualityEnterCount() > enteredBefore, TimeSpan.FromSeconds(5)));
                disposal = Task.Run(left.Dispose);
                await Task.Delay(100);
                Assert.False(disposal.IsCompleted);
            }
            finally
            {
                controls.SetEqualityWait(0);
            }
            Assert.True(await comparison.WaitAsync(TimeSpan.FromSeconds(5)));
            await disposal!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<ObjectDisposedException>(() => left.HasSameNativeContent(right));
        }

        Assert.Equal(0, controls.M2LiveCount());
        Assert.Equal(0, controls.ProgramLiveCount());
    }

    private static void Repeat(int count, Action action)
    {
        for (var index = 0; index < count; index++) action();
    }

    private static string GetFakeNativePath()
    {
        var name = OperatingSystem.IsWindows() ? "migraphx_c.dll" : "libmigraphx_c.so";
        return Path.Combine(FindRepositoryRoot(), "artifacts", "fake-native", "Release", name);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the MIGraphXSharp repository root.");
    }

    private sealed class FakeControls : IDisposable
    {
        private readonly IntPtr library;
        private readonly VoidDelegate reset;
        private readonly SetStringIntDelegate setFailure;
        private readonly SetStringDelegate setInvalidBool;
        private readonly SetIntDelegate setRegistryMode;
        private readonly SetIntDelegate setEqualityWait;
        private readonly GetIntDelegate equalityEnterCount;
        private readonly GetIntDelegate m2LiveCount;
        private readonly GetIntDelegate programLiveCount;
        private readonly GetNameDelegate getName;

        internal FakeControls(string path)
        {
            library = NativeLibrary.Load(path);
            reset = Get<VoidDelegate>("fake_reset");
            setFailure = Get<SetStringIntDelegate>("fake_set_failure");
            setInvalidBool = Get<SetStringDelegate>("fake_set_invalid_bool");
            setRegistryMode = Get<SetIntDelegate>("fake_set_onnx_registry_mode");
            setEqualityWait = Get<SetIntDelegate>("fake_set_equality_wait");
            equalityEnterCount = Get<GetIntDelegate>("fake_equality_enter_count");
            m2LiveCount = Get<GetIntDelegate>("fake_m2_live_count");
            programLiveCount = Get<GetIntDelegate>("fake_program_live_count");
            getName = Get<GetNameDelegate>("migraphx_get_onnx_operator_name_at_index");
        }

        internal void Reset() => reset();
        internal void SetFailure(string entryPoint, int status) => setFailure(entryPoint, status);
        internal void SetInvalidBool(string entryPoint) => setInvalidBool(entryPoint);
        internal void SetRegistryMode(int value) => setRegistryMode(value);
        internal void SetEqualityWait(int value) => setEqualityWait(value);
        internal int EqualityEnterCount() => equalityEnterCount();
        internal int M2LiveCount() => m2LiveCount();
        internal int ProgramLiveCount() => programLiveCount();

        internal int GetNameAtIndex(int index)
        {
            var slot = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(slot, IntPtr.Zero);
                return getName(slot, new UIntPtr(checked((uint)index)));
            }
            finally
            {
                Marshal.FreeHGlobal(slot);
            }
        }

        public void Dispose() => NativeLibrary.Free(library);
        private T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetIntDelegate(int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetIntDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetNameDelegate(IntPtr output, UIntPtr index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetStringDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetStringIntDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string value, int status);
    }
}
