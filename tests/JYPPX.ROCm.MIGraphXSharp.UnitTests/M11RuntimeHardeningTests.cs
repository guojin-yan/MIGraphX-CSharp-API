using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M11RuntimeHardeningTests
{
    [Fact]
    public void SafeNegativeMatrixRejectsAtManagedBoundariesAndReleasesOwners()
    {
        var nativePath = FakePath();
        using var controls = new FakeControls(nativePath);
        controls.Reset();
        using (var onnxOptions = new MIGraphXOnnxOptions(nativePath))
        using (var program = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, onnxOptions))
        {
            var parameter = program.GetParameterShapes().Single();
            using var argument = MIGraphXArgument.Create(nativePath, parameter.Value, new[] { 1f, 2f, 3f, 4f });
            using var wrongMap = new MIGraphXParameterMap(nativePath);
            wrongMap.Add("wrong-input", argument);
            Assert.Throws<InvalidOperationException>(() => program.Run(wrongMap));
            using var target = new MIGraphXTarget(nativePath);
            using var compileOptions = new MIGraphXCompileOptions(nativePath);
            program.Compile(target, compileOptions);
            Assert.Throws<ArgumentException>(() => program.Run(wrongMap));
            Assert.Throws<ArgumentException>(() => MIGraphXArgument.Create(nativePath, parameter.Value, new[] { 1f }));

            var disposed = MIGraphXArgument.Create(nativePath, parameter.Value, new[] { 1f, 2f, 3f, 4f });
            disposed.Dispose();
            Assert.Throws<ObjectDisposedException>(() => disposed.ToArray<float>());
        }

        Assert.Equal(0, controls.M2LiveCount());
        Assert.Equal(0, controls.ProgramLiveCount());
    }

    [Fact]
    public void CacheIdentityChangesForNativePackageModelAndOptionsWithoutReadingGlobalState()
    {
        const string zero = "0000000000000000000000000000000000000000000000000000000000000000";
        const string one = "1111111111111111111111111111111111111111111111111111111111111111";
        var baseline = new MIGraphXCacheMetadata(zero, "gpu", "offloadCopy=true", "msgpack", one, managedIdentity: "core/0.9.0-rc.5+source-a");
        var model = new MIGraphXCacheMetadata(one, "gpu", "offloadCopy=true", "msgpack", one, managedIdentity: "core/0.9.0-rc.5+source-a");
        var options = new MIGraphXCacheMetadata(zero, "gpu", "offloadCopy=false", "msgpack", one, managedIdentity: "core/0.9.0-rc.5+source-a");
        var native = new MIGraphXCacheMetadata(zero, "gpu", "offloadCopy=true", "msgpack", zero, managedIdentity: "core/0.9.0-rc.5+source-a");
        var package = new MIGraphXCacheMetadata(zero, "gpu", "offloadCopy=true", "msgpack", one, managedIdentity: "core/0.9.0-rc.5+source-b");

        Assert.Equal(5, new[] { baseline, model, options, native, package }.Select(value => value.ComputeKey()).Distinct(StringComparer.Ordinal).Count());
        Assert.Throws<ArgumentException>(() => new MIGraphXModelCache("relative-cache"));
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
        throw new DirectoryNotFoundException();
    }

    private sealed class FakeControls : IDisposable
    {
        private readonly IntPtr library;
        private readonly Action reset;
        private readonly GetInt m2LiveCount;
        private readonly GetInt programLiveCount;

        internal FakeControls(string path)
        {
            library = NativeLibrary.Load(path);
            reset = Get<Action>("fake_reset");
            m2LiveCount = Get<GetInt>("fake_m2_live_count");
            programLiveCount = Get<GetInt>("fake_program_live_count");
        }

        internal void Reset() => reset();
        internal int M2LiveCount() => m2LiveCount();
        internal int ProgramLiveCount() => programLiveCount();
        public void Dispose() => NativeLibrary.Free(library);
        private T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetInt();
    }
}
