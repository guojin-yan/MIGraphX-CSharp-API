using System.Runtime.InteropServices;
using System.Text;
using JYPPX.ROCm.MIGraphXSharp.Diagnostics;
using JYPPX.ROCm.MIGraphXSharp.Interop;
using JYPPX.ROCm.MIGraphXSharp.Loading;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M1NativeVerticalTests
{
    [Fact]
    public void FakeNativeExercisesLoaderStatusUtf8OwnershipAssignAndConcurrency()
    {
        var fakeDirectory = Path.Combine(FindRepositoryRoot(), "artifacts", "fake-native", "Release");
        var completePath = Path.Combine(fakeDirectory, OperatingSystem.IsWindows() ? "migraphx_c.dll" : "libmigraphx_c.so");
        var missingExportPath = Path.Combine(fakeDirectory, OperatingSystem.IsWindows() ? "migraphx_c_missing_export.dll" : "libmigraphx_c_missing_export.so");

        Assert.Throws<ArgumentException>(() => MIGraphXEnvironment.Probe("relative-native-library"));

        var candidates = NativeLibraryLoader.CandidateOrderForCurrentPlatform();
        Assert.StartsWith("application-rid-native:", candidates[0], StringComparison.Ordinal);
        Assert.Contains(candidates, item => item.StartsWith("application-base:", StringComparison.Ordinal));
        Assert.StartsWith("system-loader:", candidates[^1], StringComparison.Ordinal);
        Assert.Equal(MIGraphXNativeDiagnosticKind.BadImage, NativeLibraryLoader.ClassifyLoadFailure("platform error 0x8007000B"));
        Assert.Equal(MIGraphXNativeDiagnosticKind.DependencyMissing, NativeLibraryLoader.ClassifyLoadFailure("platform error 0x8007007E"));
        Assert.Equal(MIGraphXNativeDiagnosticKind.DependencyMissing, NativeLibraryLoader.ClassifyLoadFailure("DllNotFoundException (HRESULT 0x80131524): localized platform error"));
        Assert.Equal(MIGraphXNativeDiagnosticKind.DependencyMissing, NativeLibraryLoader.ClassifyLoadFailure("LoadLibraryW failed with Win32 error 126."));

        var missingFile = MIGraphXEnvironment.Probe(Path.Combine(fakeDirectory, "absent-native-library.dll"));
        Assert.Equal("not-available", missingFile.State);
        Assert.Contains(missingFile.Diagnostics, item => item.Kind == MIGraphXNativeDiagnosticKind.FileNotFound);

        var badImagePath = Path.Combine(fakeDirectory, "not-a-native-library.txt");
        File.WriteAllText(badImagePath, "not a binary", Encoding.ASCII);
        var badImage = MIGraphXEnvironment.Probe(badImagePath);
        Assert.Equal("not-available", badImage.State);
        Assert.Contains(badImage.Diagnostics, item => item.Kind == MIGraphXNativeDiagnosticKind.BadImage);
        Assert.Contains(badImage.Diagnostics, item => !string.IsNullOrWhiteSpace(item.Message));

        var missingExport = MIGraphXEnvironment.Probe(missingExportPath);
        Assert.Equal("not-available", missingExport.State);
        Assert.Contains(missingExport.Diagnostics, item => item.Kind == MIGraphXNativeDiagnosticKind.ExportMissing && item.Message.Contains("migraphx_program_create", StringComparison.Ordinal));

        var loaded = MIGraphXEnvironment.Probe(completePath, exerciseObjects: false);
        Assert.Equal("loaded", loaded.State);
        Assert.True(loaded.ExportsComplete);
        Assert.False(loaded.ObjectsExecuted);

        using var controls = new FakeControls(completePath);
        controls.Reset();
        Assert.Equal(4, controls.SizeOfStatus());
        Assert.Equal(IntPtr.Size, controls.SizeOfTargetHandle());

        Assert.Throws<ArgumentNullException>(() => NativeTargetHandle.Create(null!));
        Assert.Throws<ArgumentException>(() => NativeTargetHandle.Create(string.Empty));
        Assert.Throws<ArgumentException>(() => NativeTargetHandle.Create("gpu\0ref"));
        Assert.Throws<ArgumentException>(() => NativeTargetHandle.Create("\ud800"));

        const string nonAsciiTarget = "gpu-\u4e2d\u6587-\u03a9";
        using (NativeTargetHandle.Create(nonAsciiTarget))
        {
            Assert.Equal(nonAsciiTarget, controls.LastTargetName());
        }
        Assert.Equal(1, controls.TargetDestroyCount());

        var beforeKnownFailureDestroy = controls.TargetDestroyCount();
        controls.SetNextStatus((int)MIGraphXStatus.UnknownTarget);
        var known = Assert.Throws<MIGraphXException>(() => NativeTargetHandle.Create("gpu"));
        Assert.Equal((int)MIGraphXStatus.UnknownTarget, known.StatusCode);
        Assert.Equal(MIGraphXStatus.UnknownTarget, known.KnownStatus);
        Assert.Equal("migraphx_target_create", known.Operation);
        Assert.Equal(beforeKnownFailureDestroy + 1, controls.TargetDestroyCount());

        var beforeUnknownFailureDestroy = controls.ProgramDestroyCount();
        controls.SetNextStatus(777);
        var unknown = Assert.Throws<MIGraphXException>(() => NativeProgramHandle.Create());
        Assert.Equal(777, unknown.StatusCode);
        Assert.Null(unknown.KnownStatus);
        Assert.Contains("UnknownStatus", unknown.Message, StringComparison.Ordinal);
        Assert.Equal(beforeUnknownFailureDestroy + 1, controls.ProgramDestroyCount());

        var beforeNullDestroy = controls.TargetDestroyCount();
        controls.SetCreateNull(true);
        var nullHandle = Assert.Throws<MIGraphXException>(() => NativeTargetHandle.Create("gpu"));
        Assert.Contains("success with null handle", nullHandle.Operation, StringComparison.Ordinal);
        Assert.Equal(beforeNullDestroy, controls.TargetDestroyCount());

        var idempotent = NativeProgramHandle.Create();
        var beforeIdempotentDestroy = controls.ProgramDestroyCount();
        idempotent.Dispose();
        idempotent.Dispose();
        Assert.Equal(beforeIdempotentDestroy + 1, controls.ProgramDestroyCount());

        var destroyFailure = NativeProgramHandle.Create();
        var beforeDestroyFailure = controls.ProgramDestroyCount();
        controls.SetNextStatus((int)MIGraphXStatus.UnknownError);
        destroyFailure.Dispose();
        destroyFailure.Dispose();
        Assert.Equal(beforeDestroyFailure + 1, controls.ProgramDestroyCount());
        Assert.Equal(0, controls.ProgramLiveCount());

        var report = MIGraphXEnvironment.Probe(completePath, exerciseObjects: true, targetName: nonAsciiTarget);
        Assert.Equal("executed", report.State);
        Assert.True(report.ObjectsExecuted);
        Assert.Equal(
            report.Diagnostics.Single(item => item.Kind == MIGraphXNativeDiagnosticKind.Loaded).Source,
            report.Diagnostics.Single(item => item.Kind == MIGraphXNativeDiagnosticKind.Executed).Source);
        Assert.Equal(1, controls.TargetAssignCount());
        Assert.Equal(1, controls.ProgramAssignCount());
        Assert.Equal(1, controls.TargetAssignCopied());
        Assert.Equal(1, controls.ProgramAssignCopied());

        var targetDestroyBeforeConcurrency = controls.TargetDestroyCount();
        var programDestroyBeforeConcurrency = controls.ProgramDestroyCount();
        Parallel.For(0, 64, _ =>
        {
            using var target = NativeTargetHandle.Create("gpu");
            using var program = NativeProgramHandle.Create();
        });
        Assert.Equal(targetDestroyBeforeConcurrency + 64, controls.TargetDestroyCount());
        Assert.Equal(programDestroyBeforeConcurrency + 64, controls.ProgramDestroyCount());
        Assert.Equal(0, controls.TargetLiveCount());
        Assert.Equal(0, controls.ProgramLiveCount());
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the MIGraphXSharp repository root.");
    }

    private sealed class FakeControls : IDisposable
    {
        private readonly IntPtr library;
        private readonly ResetDelegate reset;
        private readonly SetIntDelegate setNextStatus;
        private readonly SetIntDelegate setCreateNull;
        private readonly GetIntDelegate targetDestroyCount;
        private readonly GetIntDelegate programDestroyCount;
        private readonly GetIntDelegate targetAssignCount;
        private readonly GetIntDelegate programAssignCount;
        private readonly GetIntDelegate targetLiveCount;
        private readonly GetIntDelegate programLiveCount;
        private readonly GetIntDelegate targetAssignCopied;
        private readonly GetIntDelegate programAssignCopied;
        private readonly GetIntDelegate sizeOfStatus;
        private readonly GetIntDelegate sizeOfTargetHandle;
        private readonly GetPointerDelegate lastTargetName;

        internal FakeControls(string path)
        {
            library = NativeLibrary.Load(path);
            reset = Get<ResetDelegate>("fake_reset");
            setNextStatus = Get<SetIntDelegate>("fake_set_next_status");
            setCreateNull = Get<SetIntDelegate>("fake_set_create_null");
            targetDestroyCount = Get<GetIntDelegate>("fake_target_destroy_count");
            programDestroyCount = Get<GetIntDelegate>("fake_program_destroy_count");
            targetAssignCount = Get<GetIntDelegate>("fake_target_assign_count");
            programAssignCount = Get<GetIntDelegate>("fake_program_assign_count");
            targetLiveCount = Get<GetIntDelegate>("fake_target_live_count");
            programLiveCount = Get<GetIntDelegate>("fake_program_live_count");
            targetAssignCopied = Get<GetIntDelegate>("fake_target_assign_copied");
            programAssignCopied = Get<GetIntDelegate>("fake_program_assign_copied");
            sizeOfStatus = Get<GetIntDelegate>("fake_sizeof_status");
            sizeOfTargetHandle = Get<GetIntDelegate>("fake_sizeof_target_handle");
            lastTargetName = Get<GetPointerDelegate>("fake_last_target_name");
        }

        internal void Reset() => reset();
        internal void SetNextStatus(int value) => setNextStatus(value);
        internal void SetCreateNull(bool value) => setCreateNull(value ? 1 : 0);
        internal int TargetDestroyCount() => targetDestroyCount();
        internal int ProgramDestroyCount() => programDestroyCount();
        internal int TargetAssignCount() => targetAssignCount();
        internal int ProgramAssignCount() => programAssignCount();
        internal int TargetLiveCount() => targetLiveCount();
        internal int ProgramLiveCount() => programLiveCount();
        internal int TargetAssignCopied() => targetAssignCopied();
        internal int ProgramAssignCopied() => programAssignCopied();
        internal int SizeOfStatus() => sizeOfStatus();
        internal int SizeOfTargetHandle() => sizeOfTargetHandle();
        internal string LastTargetName() => Marshal.PtrToStringUTF8(lastTargetName())!;

        public void Dispose() => NativeLibrary.Free(library);

        private T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ResetDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetIntDelegate(int value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetIntDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetPointerDelegate();
    }
}
