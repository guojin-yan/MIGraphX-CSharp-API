using System.Runtime.InteropServices;
using JYPPX.ROCm.HipSharp;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M6HipAsyncTests
{
    [Fact]
    public void HostAsyncIsQueuedAndEarlyDisposeKeepsEveryNativeOwnerAlive()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        using HipStream stream = runtime.CreateStream();
        var values = new[] { 1f, -2f, 3.5f, 4f };

        var program = CreateProgram(migraphxPath, offloadCopy: true);
        var shape = program.GetParameterShapes()["input"];
        var argument = MIGraphXArgument.Create(migraphxPath, shape, values);
        var parameters = new MIGraphXParameterMap(migraphxPath);
        parameters.Add("input", argument);
        MIGraphXHipAsyncRun run = program.RunHostAsync(parameters, stream);

        Assert.False(run.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => run.Outputs);
        Assert.Equal("hipStream_t", migraphx.LastAsyncName());
        Assert.NotEqual(IntPtr.Zero, migraphx.LastAsyncStream());
        argument.Dispose();
        parameters.Dispose();
        program.Dispose();
        Assert.True(migraphx.M2LiveCount() > 0);
        Assert.False(run.TryComplete());

        migraphx.CompleteAll();
        hip.SetQueryStatus(0);
        Assert.True(run.TryComplete());
        Assert.True(run.IsCompleted);
        Assert.Equal(values, run.Outputs[0].ToArray<float>());
        run.Dispose();
        run.Dispose();
        Assert.Equal(0, migraphx.M2LiveCount());
        Assert.Equal(0, migraphx.ProgramLiveCount());
    }

    [Fact]
    public void DeviceInputRetainsPointerAndCopiesOutputToOwnedHostMemoryAfterCompletion()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        using HipStream stream = runtime.CreateStream();
        var values = new[] { 9f, 8f, -7f, 6f };
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        HipDeviceMemory memory = runtime.Allocate((ulong)bytes.Length);
        memory.CopyFrom(bytes);
        var pointer = memory.DangerousGetHandle();

        using var program = CreateProgram(migraphxPath, offloadCopy: false);
        var shape = program.GetParameterShapes()["input"];
        using MIGraphXHipAsyncRun run = program.RunDeviceAsync(
            new[] { new MIGraphXHipDeviceInput("input", shape, memory) }, stream);
        Assert.Equal(pointer, migraphx.LastAsyncInput());
        memory.Dispose();
        Assert.Equal(0, hip.FreeCount());
        Assert.False(run.TryComplete());

        migraphx.CompleteAll();
        hip.SetQueryStatus(0);
        Assert.True(run.TryComplete());
        Assert.Equal(values, run.Outputs[0].ToArray<float>());
        Assert.Equal(1, hip.FreeCount());
        Assert.True(hip.MemcpyCount() >= 2);
    }

    [Fact]
    public void DeviceInputValidationFailsBeforeNativeEnqueue()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var firstRuntime = new HipRuntime(HipPath());
        using var secondRuntime = new HipRuntime(HipPath());
        using HipStream stream = firstRuntime.CreateStream();
        using HipDeviceMemory memory = secondRuntime.Allocate(16);
        using var program = CreateProgram(migraphxPath, offloadCopy: false);
        var shape = program.GetParameterShapes()["input"];

        Assert.Throws<ArgumentException>(() => program.RunDeviceAsync(
            new[] { new MIGraphXHipDeviceInput("input", shape, memory) }, stream));
        using HipDeviceMemory tooSmall = firstRuntime.Allocate(4);
        Assert.Throws<ArgumentException>(() => program.RunDeviceAsync(
            new[] { new MIGraphXHipDeviceInput("input", shape, tooSmall) }, stream));
        var dynamic = MIGraphXShape.CreateDynamic(MIGraphXShapeDataType.Float32, new[] { MIGraphXDynamicDimension.Range(1, 4) });
        Assert.Throws<ArgumentException>(() => program.RunDeviceAsync(
            new[] { new MIGraphXHipDeviceInput("input", dynamic, tooSmall) }, stream));

        stream.BeginCapture();
        using HipDeviceMemory valid = firstRuntime.Allocate(16);
        Assert.Throws<InvalidOperationException>(() => program.RunDeviceAsync(
            new[] { new MIGraphXHipDeviceInput("input", shape, valid) }, stream));
        stream.EndCapture().Dispose();
        Assert.Equal(0, migraphx.AsyncRunCount());
    }

    [Fact]
    public void EnqueueFailuresCleanReturnedCollectionsAndInputLeases()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        using HipStream stream = runtime.CreateStream();
        using var program = CreateProgram(migraphxPath, offloadCopy: true);
        var shape = program.GetParameterShapes()["input"];
        using var argument = MIGraphXArgument.Create(migraphxPath, shape, new[] { 1f, 2f, 3f, 4f });
        using var parameters = new MIGraphXParameterMap(migraphxPath);
        parameters.Add("input", argument);
        migraphx.SetFailure("migraphx_program_run_async", (int)MIGraphXStatus.UnknownError);
        var error = Assert.Throws<MIGraphXException>(() => program.RunHostAsync(parameters, stream));
        Assert.Equal("migraphx_program_run_async", error.Operation);
        Assert.Equal(0, migraphx.AsyncCompleteCount());
    }

    [Fact]
    public void SuccessWithNullAndCompletionSnapshotFailuresReleaseAllOwners()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        using HipStream stream = runtime.CreateStream();
        using var program = CreateProgram(migraphxPath, offloadCopy: true);
        var shape = program.GetParameterShapes()["input"];
        using var argument = MIGraphXArgument.Create(migraphxPath, shape, new[] { 1f, 2f, 3f, 4f });
        using var parameters = new MIGraphXParameterMap(migraphxPath);
        parameters.Add("input", argument);
        var liveBefore = migraphx.M2LiveCount();

        migraphx.SetNullOutput("migraphx_program_run_async");
        Assert.Throws<MIGraphXException>(() => program.RunHostAsync(parameters, stream));
        Assert.Equal(0, migraphx.AsyncRunCount());

        using MIGraphXHipAsyncRun nullOutput = program.RunHostAsync(parameters, stream);
        migraphx.CompleteAll();
        migraphx.SetShapeMode(9);
        hip.SetQueryStatus(0);
        Assert.Throws<MIGraphXException>(() => nullOutput.TryComplete());
        Assert.True(nullOutput.IsCompleted);
        Assert.Throws<MIGraphXException>(() => nullOutput.Dispose());
        Assert.Equal(liveBefore, migraphx.M2LiveCount());

        migraphx.SetShapeMode(0);
        using MIGraphXHipAsyncRun drifting = program.RunHostAsync(parameters, stream);
        migraphx.CompleteAll();
        migraphx.SetShapeMode(16);
        Assert.Throws<InvalidOperationException>(() => drifting.TryComplete());
        Assert.True(drifting.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => drifting.Dispose());
        Assert.Equal(liveBefore, migraphx.M2LiveCount());
    }

    [Fact]
    public void DifferentStreamsKeepNativeCompletionIsolated()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        using HipStream firstStream = runtime.CreateStream();
        using HipStream secondStream = runtime.CreateStream();
        using var program = CreateProgram(migraphxPath, offloadCopy: true);
        var shape = program.GetParameterShapes()["input"];
        using var argument = MIGraphXArgument.Create(migraphxPath, shape, new[] { 3f, 1f, 4f, 1f });
        using var parameters = new MIGraphXParameterMap(migraphxPath);
        parameters.Add("input", argument);

        using MIGraphXHipAsyncRun first = program.RunHostAsync(parameters, firstStream);
        var firstPointer = migraphx.LastAsyncStream();
        using MIGraphXHipAsyncRun second = program.RunHostAsync(parameters, secondStream);
        var secondPointer = migraphx.LastAsyncStream();
        Assert.NotEqual(firstPointer, secondPointer);

        migraphx.CompleteStream(firstPointer);
        hip.SetQueryStatus(0);
        Assert.True(first.TryComplete());
        Assert.False(second.IsCompleted);
        hip.SetQueryStatus(600);
        Assert.False(second.TryComplete());

        migraphx.CompleteStream(secondPointer);
        hip.SetQueryStatus(0);
        Assert.True(second.TryComplete());
        Assert.Equal(first.Outputs[0].ToArray<float>(), second.Outputs[0].ToArray<float>());
    }

    [Fact]
    public void SameStreamCompletesMultipleRunsWithIndependentOutputSnapshots()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        using HipStream stream = runtime.CreateStream();
        using var program = CreateProgram(migraphxPath, offloadCopy: true);
        var shape = program.GetParameterShapes()["input"];
        var firstValues = new[] { 1f, 2f, 3f, 4f };
        var secondValues = new[] { 5f, 6f, 7f, 8f };
        using var firstArgument = MIGraphXArgument.Create(migraphxPath, shape, firstValues);
        using var secondArgument = MIGraphXArgument.Create(migraphxPath, shape, secondValues);
        using var firstParameters = new MIGraphXParameterMap(migraphxPath);
        using var secondParameters = new MIGraphXParameterMap(migraphxPath);
        firstParameters.Add("input", firstArgument);
        secondParameters.Add("input", secondArgument);

        using MIGraphXHipAsyncRun first = program.RunHostAsync(firstParameters, stream);
        using MIGraphXHipAsyncRun second = program.RunHostAsync(secondParameters, stream);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        migraphx.CompleteAll();
        hip.SetQueryStatus(0);
        Assert.True(first.TryComplete());
        Assert.True(second.IsCompleted);
        Assert.Equal(firstValues, first.Outputs[0].ToArray<float>());
        Assert.Equal(secondValues, second.Outputs[0].ToArray<float>());
    }

    [Fact]
    public void SynchronizeAndPendingResultDisposeEstablishCompletionBoundary()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        using HipStream stream = runtime.CreateStream();
        using var program = CreateProgram(migraphxPath, offloadCopy: true);
        var shape = program.GetParameterShapes()["input"];
        using var argument = MIGraphXArgument.Create(migraphxPath, shape, new[] { 2f, 7f, 1f, 8f });
        using var parameters = new MIGraphXParameterMap(migraphxPath);
        parameters.Add("input", argument);

        using (MIGraphXHipAsyncRun synchronized = program.RunHostAsync(parameters, stream))
        {
            migraphx.CompleteAll();
            synchronized.Synchronize();
            Assert.True(synchronized.IsCompleted);
            Assert.Equal(new[] { 2f, 7f, 1f, 8f }, synchronized.Outputs[0].ToArray<float>());
        }

        MIGraphXHipAsyncRun disposedWhilePending = program.RunHostAsync(parameters, stream);
        migraphx.CompleteAll();
        disposedWhilePending.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposedWhilePending.IsCompleted);
    }

    [Fact]
    public void StreamDisposeCompletesPendingRunBeforeDestroyingStream()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        HipStream stream = runtime.CreateStream();
        using var program = CreateProgram(migraphxPath, offloadCopy: true);
        var shape = program.GetParameterShapes()["input"];
        var values = new[] { 9f, 3f, 7f, 5f };
        using var argument = MIGraphXArgument.Create(migraphxPath, shape, values);
        using var parameters = new MIGraphXParameterMap(migraphxPath);
        parameters.Add("input", argument);
        using MIGraphXHipAsyncRun run = program.RunHostAsync(parameters, stream);

        migraphx.CompleteAll();
        stream.Dispose();
        Assert.True(stream.IsDisposed);
        Assert.True(run.IsCompleted);
        Assert.Equal(values, run.Outputs[0].ToArray<float>());
    }

    [Fact]
    public void DeviceValidationRejectsCrossDeviceAndCompletionReportsD2HFailure()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();
        using var runtime = new HipRuntime(HipPath());
        hip.SetDevice(0);
        using HipStream stream = runtime.CreateStream();
        hip.SetDevice(1);
        using HipDeviceMemory wrongDevice = runtime.Allocate(16);
        using var program = CreateProgram(migraphxPath, offloadCopy: false);
        var shape = program.GetParameterShapes()["input"];
        Assert.Throws<ArgumentException>(() => program.RunDeviceAsync(
            new[] { new MIGraphXHipDeviceInput("input", shape, wrongDevice) }, stream));

        hip.SetDevice(0);
        HipDeviceMemory memory = runtime.Allocate(16);
        memory.CopyFrom(new byte[16]);
        using MIGraphXHipAsyncRun run = program.RunDeviceAsync(
            new[] { new MIGraphXHipDeviceInput("input", shape, memory) }, stream);
        memory.Dispose();
        migraphx.CompleteAll();
        hip.SetMemcpyStatus(1);
        hip.SetQueryStatus(0);
        Assert.Throws<HipException>(() => run.TryComplete());
        Assert.True(run.IsCompleted);
        Assert.Equal(1, hip.FreeCount());
        Assert.Throws<HipException>(() => run.Dispose());
    }

    [Fact]
    public void CompletedRunFinalizerReleasesOwnedOutputWithoutWaiting()
    {
        var migraphxPath = MIGraphXPath();
        using var migraphx = new MIGraphXControls(migraphxPath);
        using var hip = new HipControls(HipPath());
        migraphx.Reset();
        hip.Reset();

        WeakReference abandoned = CreateCompletedRunWithoutDisposal(migraphxPath, migraphx, hip);
        for (var attempt = 0; attempt < 3 && abandoned.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(abandoned.IsAlive);
        Assert.Equal(0, migraphx.M2LiveCount());
        Assert.Equal(0, migraphx.ProgramLiveCount());
    }

    private static WeakReference CreateCompletedRunWithoutDisposal(string path, MIGraphXControls migraphx, HipControls hip)
    {
        using var runtime = new HipRuntime(HipPath());
        using HipStream stream = runtime.CreateStream();
        using var program = CreateProgram(path, offloadCopy: true);
        var shape = program.GetParameterShapes()["input"];
        using var argument = MIGraphXArgument.Create(path, shape, new[] { 2f, 7f, 1f, 8f });
        using var parameters = new MIGraphXParameterMap(path);
        parameters.Add("input", argument);
        var run = program.RunHostAsync(parameters, stream);
        migraphx.CompleteAll();
        hip.SetQueryStatus(0);
        Assert.True(run.TryComplete());
        Assert.Single(run.Outputs);
        return new WeakReference(run);
    }

    private static MIGraphXProgram CreateProgram(string path, bool offloadCopy)
    {
        using var options = new MIGraphXOnnxOptions(path);
        var program = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options);
        try
        {
            using var target = new MIGraphXTarget(path);
            using var compileOptions = new MIGraphXCompileOptions(path, offloadCopy);
            program.Compile(target, compileOptions);
            return program;
        }
        catch { program.Dispose(); throw; }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private static string MIGraphXPath() => Path.Combine(RepositoryRoot(), "artifacts", "fake-native", "Release", OperatingSystem.IsWindows() ? "migraphx_c.dll" : "libmigraphx_c.so");
    private static string HipPath() => Path.Combine(RepositoryRoot(), "artifacts", "fake-hip", "Release", OperatingSystem.IsWindows() ? "amdhip64.dll" : "libamdhip64.so");

    private abstract class Controls : IDisposable
    {
        private readonly IntPtr library;
        protected Controls(string path) => library = NativeLibrary.Load(path);
        protected T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        public void Dispose() => NativeLibrary.Free(library);
    }

    private sealed class MIGraphXControls : Controls
    {
        private readonly VoidDelegate reset;
        private readonly VoidDelegate completeAll;
        private readonly CompleteStreamDelegate completeStream;
        private readonly GetIntDelegate m2Live;
        private readonly GetIntDelegate programLive;
        private readonly GetIntDelegate asyncRuns;
        private readonly GetIntDelegate asyncCompletes;
        private readonly GetPointerDelegate lastStream;
        private readonly GetPointerDelegate lastInput;
        private readonly GetPointerDelegate lastName;
        private readonly SetStringIntDelegate setFailure;
        private readonly SetStringDelegate setNullOutput;
        private readonly SetIntDelegate setShapeMode;
        internal MIGraphXControls(string path) : base(path)
        {
            reset = Get<VoidDelegate>("fake_reset"); completeAll = Get<VoidDelegate>("fake_complete_all_streams");
            completeStream = Get<CompleteStreamDelegate>("fake_complete_stream");
            m2Live = Get<GetIntDelegate>("fake_m2_live_count"); programLive = Get<GetIntDelegate>("fake_program_live_count");
            asyncRuns = Get<GetIntDelegate>("fake_async_run_count"); asyncCompletes = Get<GetIntDelegate>("fake_async_complete_count");
            lastStream = Get<GetPointerDelegate>("fake_last_async_stream"); lastInput = Get<GetPointerDelegate>("fake_last_async_input");
            lastName = Get<GetPointerDelegate>("fake_last_async_name"); setFailure = Get<SetStringIntDelegate>("fake_set_failure");
            setNullOutput = Get<SetStringDelegate>("fake_set_null_output"); setShapeMode = Get<SetIntDelegate>("fake_set_shape_mode");
        }
        internal void Reset() => reset(); internal void CompleteAll() => completeAll();
        internal void CompleteStream(IntPtr stream) => completeStream(stream);
        internal int M2LiveCount() => m2Live(); internal int ProgramLiveCount() => programLive();
        internal int AsyncRunCount() => asyncRuns(); internal int AsyncCompleteCount() => asyncCompletes();
        internal IntPtr LastAsyncStream() => lastStream(); internal IntPtr LastAsyncInput() => lastInput();
        internal string LastAsyncName() => Marshal.PtrToStringUTF8(lastName())!;
        internal void SetFailure(string name, int status) => setFailure(name, status);
        internal void SetNullOutput(string name) => setNullOutput(name);
        internal void SetShapeMode(int mode) => setShapeMode(mode);
    }

    private sealed class HipControls : Controls
    {
        private readonly VoidDelegate reset;
        private readonly SetIntDelegate setQuery;
        private readonly SetIntDelegate setDevice;
        private readonly SetIntDelegate setMemcpyStatus;
        private readonly GetIntDelegate freeCount;
        private readonly GetIntDelegate memcpyCount;
        internal HipControls(string path) : base(path)
        {
            reset = Get<VoidDelegate>("fake_hip_reset"); setQuery = Get<SetIntDelegate>("fake_hip_set_query_status");
            setDevice = Get<SetIntDelegate>("fake_hip_set_device"); setMemcpyStatus = Get<SetIntDelegate>("fake_hip_set_memcpy_status");
            freeCount = Get<GetIntDelegate>("fake_hip_free_count"); memcpyCount = Get<GetIntDelegate>("fake_hip_memcpy_count");
        }
        internal void Reset() => reset(); internal void SetQueryStatus(int status) => setQuery(status);
        internal void SetDevice(int device) => setDevice(device); internal void SetMemcpyStatus(int status) => setMemcpyStatus(status);
        internal int FreeCount() => freeCount(); internal int MemcpyCount() => memcpyCount();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetIntDelegate(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CompleteStreamDelegate(IntPtr stream);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetIntDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetPointerDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate void SetStringIntDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string value, int status);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate void SetStringDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
}
