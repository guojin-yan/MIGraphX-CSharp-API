using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JYPPX.ROCm.MIGraphXSharp.Interop;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class M5DynamicShapeCacheTests
{
    [Fact]
    public void DynamicDimensionAndShapeHaveExplicitValueSemantics()
    {
        var dimension = new MIGraphXDynamicDimension(1, 8, new long[] { 2, 4 });
        Assert.False(dimension.IsFixed);
        Assert.Equal(new long[] { 2, 4 }, dimension.Optimals);
        Assert.Equal(dimension, new MIGraphXDynamicDimension(1, 8, new long[] { 2, 4 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MIGraphXDynamicDimension(8, 1));
        Assert.Throws<ArgumentException>(() => new MIGraphXDynamicDimension(1, 8, new long[] { 4, 4 }));
        Assert.Throws<ArgumentException>(() => new MIGraphXDynamicDimension(1, 8, new long[] { 4, 2 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MIGraphXDynamicDimension(1, 8, new long[] { 9 }));
        Assert.Throws<OverflowException>(() => new MIGraphXDynamicDimension(-1));
        Assert.True(MIGraphXDynamicDimension.Fixed(0).IsFixed);
        Assert.Equal(UIntPtr.Size == 4 ? uint.MaxValue : long.MaxValue, MIGraphXDynamicDimension.Unknown.Maximum);

        var shape = MIGraphXShape.CreateDynamic(MIGraphXShapeDataType.Float32, new[] { dimension, MIGraphXDynamicDimension.Fixed(4) });
        Assert.True(shape.IsDynamic);
        Assert.Equal(2, shape.Rank);
        Assert.Throws<InvalidOperationException>(() => _ = shape.ElementCount);
        Assert.Throws<InvalidOperationException>(() => _ = shape.Lengths);

        var path = FakePath();
        using var native = NativeShapeHandle.CreateDynamic(shape);
        var snapshot = MIGraphXShape.FromNative(native.DangerousGetHandle(), "dynamic shape", shape.DynamicDimensions);
        Assert.True(snapshot.IsDynamic);

        using var nativeLeft = NativeDynamicDimensionHandle.Create(dimension);
        using var nativeRight = NativeDynamicDimensionHandle.Create(new MIGraphXDynamicDimension(1, 8, new long[] { 2, 4 }));
        Assert.True(nativeLeft.EqualsValue(nativeRight));

        var scalar = MIGraphXShape.CreateDynamic(MIGraphXShapeDataType.Float32, Array.Empty<MIGraphXDynamicDimension>());
        using var nativeScalar = NativeShapeHandle.CreateDynamic(scalar);
        Assert.Empty(MIGraphXShape.FromNative(nativeScalar.DangerousGetHandle(), "dynamic scalar", scalar.DynamicDimensions).DynamicDimensions);
    }

    [Fact]
    public void DynamicOnnxOverrideIsCopiedIntoShapeSnapshot()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();
        using var options = new MIGraphXOnnxOptions(path);
        options.SetInputParameterShape("input", new long[] { 2, 4 });
        options.SetDefaultDimensionValue(4);
        using (var staticProgram = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options))
        {
            Assert.Equal(new long[] { 2, 4 }, staticProgram.GetParameterShapes()["input"].Lengths);
        }
        Assert.Throws<ArgumentException>(() => options.SetInputParameterShape("\ud800", new long[] { 1 }));
        controls.SetFailure("migraphx_dynamic_dimension_create_min_max", (int)MIGraphXStatus.UnknownError);
        Assert.Throws<MIGraphXException>(() => options.SetDynamicInputParameterShape("input", new[] { new MIGraphXDynamicDimension(1, 8) }));
        Assert.Equal(0, controls.M5LiveCount());
        options.SetDynamicInputParameterShape("input", new[] { new MIGraphXDynamicDimension(1, 8, new long[] { 4 }), MIGraphXDynamicDimension.Fixed(4) });
        options.SetDefaultDynamicDimensionValue(new MIGraphXDynamicDimension(1, 8));
        using var program = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options);
        var shape = program.GetParameterShapes()["input"];
        Assert.True(shape.IsDynamic);
        Assert.Equal(new long[] { 1, 8 }, new[] { shape.DynamicDimensions[0].Minimum, shape.DynamicDimensions[0].Maximum });
        Assert.True(shape.DynamicDimensions[1].IsFixed);
    }

    [Fact]
    public void DynamicCollectionNullAndDriftFailuresReleaseOwnedTemporaries()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        var shape = MIGraphXShape.CreateDynamic(MIGraphXShapeDataType.Float32, new[] { new MIGraphXDynamicDimension(1, 8), MIGraphXDynamicDimension.Fixed(4) });

        controls.Reset();
        using (var native = NativeShapeHandle.CreateDynamic(shape))
        {
            controls.SetNullOutput("migraphx_dynamic_dimensions_get");
            Assert.Throws<MIGraphXException>(() => MIGraphXShape.FromNative(native.DangerousGetHandle(), "null borrowed dimension", shape.DynamicDimensions));
        }
        Assert.Equal(0, controls.M5LiveCount());

        controls.Reset();
        controls.SetShapeMode(15);
        using (var native = NativeShapeHandle.CreateDynamic(shape))
        {
            Assert.Throws<InvalidOperationException>(() => MIGraphXShape.FromNative(native.DangerousGetHandle(), "drifting dimensions", shape.DynamicDimensions));
        }
        Assert.Equal(0, controls.M5LiveCount());
    }

    [Fact]
    public void FileOptionsAndLoadFailClosedWithoutLeakingHandles()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);

        controls.Reset();
        controls.SetFailure("migraphx_file_options_create", (int)MIGraphXStatus.UnknownError);
        Assert.Throws<MIGraphXException>(() => new MIGraphXFileOptions(path));
        Assert.Equal(0, controls.M5LiveCount());

        controls.Reset();
        controls.SetNullOutput("migraphx_file_options_create");
        Assert.Throws<MIGraphXException>(() => new MIGraphXFileOptions(path));
        Assert.Equal(0, controls.M5LiveCount());

        controls.Reset();
        controls.SetFailure("migraphx_file_options_set_file_format", (int)MIGraphXStatus.UnknownError);
        Assert.Throws<MIGraphXException>(() => new MIGraphXFileOptions(path));
        Assert.Equal(0, controls.M5LiveCount());

        controls.Reset();
        var saved = Path.Combine(Path.GetTempPath(), "migraphx-load-null-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(saved, new byte[] { 1 });
        try
        {
            var fileOptions = new MIGraphXFileOptions(path);
            controls.SetNullOutput("migraphx_load");
            Assert.Throws<MIGraphXException>(() => MIGraphXProgram.Load(saved, fileOptions));
            fileOptions.Dispose();
            Assert.Equal(0, controls.M5LiveCount());
        }
        finally { File.Delete(saved); }
    }

    [Fact]
    public void SaveLoadAndCacheUseOwnedProgramsAndDeterministicMetadata()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();
        var root = Path.Combine(Path.GetTempPath(), "migraphx-m5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var options = new MIGraphXOnnxOptions(path);
            using var program = MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options);
            using var fileOptions = new MIGraphXFileOptions(path);
            var savePath = Path.Combine(root, "saved.migraphx");
            program.Save(savePath, fileOptions);
            Assert.True(File.Exists(savePath));
            program.Dispose();
            using var loaded = MIGraphXProgram.Load(savePath, fileOptions);
            Assert.False(loaded.IsCompiled);

            var metadata = new MIGraphXCacheMetadata(new string('0', 64), "gpu", "offloadCopy=true", "msgpack", new string('1', 64));
            var staticScalar = new MIGraphXCacheMetadata(new string('0', 64), "gpu", "offloadCopy=true", "msgpack", new string('1', 64), new[] { new MIGraphXCacheOverride("input", Array.Empty<long>()) });
            var dynamicScalar = new MIGraphXCacheMetadata(new string('0', 64), "gpu", "offloadCopy=true", "msgpack", new string('1', 64), new[] { new MIGraphXCacheOverride("input", Array.Empty<MIGraphXDynamicDimension>()) });
            Assert.NotEqual(staticScalar.ComputeKey(), dynamicScalar.ComputeKey());
            Assert.False(staticScalar.InputOverrides[0].IsDynamic);
            Assert.True(dynamicScalar.InputOverrides[0].IsDynamic);
            var cache = new MIGraphXModelCache(root);
            using var rebuilt = cache.GetOrBuild(metadata, fileOptions, () => MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options));
            Assert.Equal(MIGraphXCacheLookupKind.Rebuilt, rebuilt.Kind);
            using var hit = cache.GetOrBuild(metadata, fileOptions, () => throw new InvalidOperationException("cache should hit"));
            Assert.Equal(MIGraphXCacheLookupKind.Hit, hit.Kind);

            File.AppendAllText(Path.Combine(root, metadata.ComputeKey() + ".migraphx"), "tampered");
            using var repaired = cache.GetOrBuild(metadata, fileOptions, () => MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options));
            Assert.Equal(MIGraphXCacheLookupKind.Rebuilt, repaired.Kind);
            Assert.Equal(MIGraphXCacheLookupKind.Corrupt, repaired.PreviousLookup);

            var changedTarget = new MIGraphXCacheMetadata(new string('0', 64), "ref", "offloadCopy=true", "msgpack", new string('1', 64));
            using var changed = cache.GetOrBuild(changedTarget, fileOptions, () => MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options));
            Assert.Equal(MIGraphXCacheLookupKind.Miss, changed.PreviousLookup);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) { Directory.Delete(root, true); }
        }
    }

    [Fact]
    public async Task CacheSerializesConcurrentWritersForTheSameKey()
    {
        var path = FakePath();
        using var controls = new FakeControls(path);
        controls.Reset();
        var root = Path.Combine(Path.GetTempPath(), "migraphx-m5-concurrent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var options = new MIGraphXOnnxOptions(path);
            using var fileOptions = new MIGraphXFileOptions(path);
            var metadata = new MIGraphXCacheMetadata(new string('2', 64), "gpu", "offloadCopy=true", "msgpack", new string('3', 64));
            var cache = new MIGraphXModelCache(root);
            var builds = 0;
            Func<MIGraphXProgram> builder = () =>
            {
                Interlocked.Increment(ref builds);
                Thread.Sleep(25);
                return MIGraphXProgram.ParseOnnxBuffer(new byte[] { 1 }, options);
            };
            var tasks = new[]
            {
                Task.Run(() => cache.GetOrBuild(metadata, fileOptions, builder)),
                Task.Run(() => cache.GetOrBuild(metadata, fileOptions, builder)),
            };
            var results = await Task.WhenAll(tasks);
            using var first = results[0];
            using var second = results[1];
            Assert.Equal(1, builds);
            Assert.Contains(MIGraphXCacheLookupKind.Rebuilt, results.Select(result => result.Kind));
            Assert.Contains(MIGraphXCacheLookupKind.Hit, results.Select(result => result.Kind));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) { Directory.Delete(root, true); }
        }
    }

    private static string FakePath()
    {
        var root = FindRoot();
        return Path.Combine(root, "artifacts", "fake-native", "Release", OperatingSystem.IsWindows() ? "migraphx_c.dll" : "libmigraphx_c.so");
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MIGraphXSharp.sln"))) { return directory.FullName; }
        }
        throw new DirectoryNotFoundException();
    }

    private sealed class FakeControls : IDisposable
    {
        private readonly IntPtr library;
        private readonly Action reset;
        private readonly SetFailureDelegate setFailure;
        private readonly SetStringDelegate setNullOutput;
        private readonly SetIntDelegate setShapeMode;
        private readonly GetIntDelegate m5LiveCount;
        internal FakeControls(string path)
        {
            library = System.Runtime.InteropServices.NativeLibrary.Load(path);
            reset = Get<Action>("fake_reset");
            setFailure = Get<SetFailureDelegate>("fake_set_failure");
            setNullOutput = Get<SetStringDelegate>("fake_set_null_output");
            setShapeMode = Get<SetIntDelegate>("fake_set_shape_mode");
            m5LiveCount = Get<GetIntDelegate>("fake_m5_live_count");
        }
        internal void Reset() => reset();
        internal void SetFailure(string entryPoint, int status) => setFailure(entryPoint, status);
        internal void SetNullOutput(string entryPoint) => setNullOutput(entryPoint);
        internal void SetShapeMode(int value) => setShapeMode(value);
        internal int M5LiveCount() => m5LiveCount();
        public void Dispose() => System.Runtime.InteropServices.NativeLibrary.Free(library);
        private T Get<T>(string name) where T : Delegate => System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(System.Runtime.InteropServices.NativeLibrary.GetExport(library, name));
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void SetFailureDelegate([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string entryPoint, int status);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void SetStringDelegate([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string value);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate void SetIntDelegate(int value);
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate int GetIntDelegate();
    }
}
