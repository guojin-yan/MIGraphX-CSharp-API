using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Interop;
using Xunit;

namespace JYPPX.ROCm.MIGraphXSharp.UnitTests;

public sealed class OptionalRuntimeTests
{
    private static string FakePath() => Path.Combine(RepositoryPaths.Artifacts, "fake-native", "Release", OperatingSystem.IsWindows() ? "migraphx_c.dll" : "libmigraphx_c.so");

    [Fact]
    public void OlderRuntimeRetainsBaselineAndRejectsOptionalApisBeforePInvoke()
    {
        var path = FakePath();
        using var baseline = new MIGraphXCompileOptions(path);
        Assert.True(MIGraphXCapabilities.IsSupported(path, MIGraphXFeature.TensorFlow));
        foreach (var feature in Enum.GetValues<MIGraphXFeature>().Where(feature => feature != MIGraphXFeature.TensorFlow))
        {
            Assert.False(MIGraphXCapabilities.IsSupported(path, feature));
            Assert.NotEmpty(MIGraphXCapabilities.GetMissingExports(path, feature));
        }
        Assert.Throws<NotSupportedException>(() => new MIGraphXCompileOptions(path, true, false, false, MIGraphXCompileMode.Balanced, null));
        Assert.Throws<NotSupportedException>(() => new MIGraphXTarget(path, "gpu", "{}"));
        using var options = new MIGraphXOnnxOptions(path);
        Assert.Throws<NotSupportedException>(() => options.SetUseDebugSymbols(true));
        Assert.Throws<NotSupportedException>(() => options.SetDimensionParameter("N", new(1, 2)));
        using var program = new MIGraphXProgram(path);
        using var parameters = new MIGraphXParameterMap(path);
        Assert.Throws<NotSupportedException>(() => program.RunTrace(parameters, _ => { }));
        var symbolic = MIGraphXDynamicDimension.Symbolic("N", new Dictionary<string, MIGraphXDynamicDimension> { ["N"] = new(1, 4) });
        Assert.Throws<NotSupportedException>(() => options.SetDefaultDynamicDimensionValue(symbolic));
        Assert.Throws<ArgumentOutOfRangeException>(() => MIGraphXCapabilities.IsSupported(path, (MIGraphXFeature)99));
    }

    [Fact]
    public void SymbolicValueAndCacheIdentityIncludeExpressionAndOrderedBounds()
    {
        var bounds = new Dictionary<string, MIGraphXDynamicDimension> { ["N"] = new(1, 8), ["M"] = new(2, 4) };
        var dimension = MIGraphXDynamicDimension.Symbolic("N+M", bounds);
        bounds.Clear();
        var reordered = MIGraphXDynamicDimension.Symbolic("N+M", new Dictionary<string, MIGraphXDynamicDimension> { ["M"] = new(2, 4), ["N"] = new(1, 8) });
        var expression = MIGraphXDynamicDimension.Symbolic("N*M", reordered.Symbols);
        var bound = MIGraphXDynamicDimension.Symbolic("N+M", new Dictionary<string, MIGraphXDynamicDimension> { ["M"] = new(2, 5), ["N"] = new(1, 8) });
        Assert.Equal(2, dimension.Symbols.Count);
        Assert.True(dimension.IsSymbolic);
        Assert.False(dimension.IsFixed);
        Assert.Equal(dimension, dimension.Clone());
        Assert.Equal(dimension, reordered);
        Assert.Equal(dimension.GetHashCode(), reordered.GetHashCode());
        Assert.NotEqual(dimension, expression);
        Assert.NotEqual(dimension, bound);
        string Key(MIGraphXDynamicDimension value) => new MIGraphXCacheMetadata(new string('0', 64), "gpu", "default", "msgpack", new string('1', 64), new[] { new MIGraphXCacheOverride("input", new[] { value }) }).ComputeKey();
        Assert.Equal(Key(dimension), Key(reordered));
        Assert.Equal(3, new[] { Key(dimension), Key(expression), Key(bound) }.Distinct().Count());
        Assert.Throws<ArgumentException>(() => MIGraphXDynamicDimension.Symbolic("N", new Dictionary<string, MIGraphXDynamicDimension> { ["N"] = dimension }));
        Assert.Throws<ArgumentException>(() => MIGraphXDynamicDimension.Symbolic("N\0", reordered.Symbols));
    }

    [Fact]
    public void ReviewedExtensionDeclarationsMatchPinnedHeaderAndExplicitAbi()
    {
        var methods = typeof(NativeMethods).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        foreach (var feature in Enum.GetValues<MIGraphXFeature>().Where(feature => feature != MIGraphXFeature.TensorFlow))
        foreach (var export in NativeFeatures.Exports(feature))
        {
            var method = Assert.Single(methods, method => method.GetCustomAttributes(typeof(LibraryImportAttribute), false)
                .Cast<LibraryImportAttribute>().Any(attribute => attribute.EntryPoint == export));
            Assert.Equal(typeof(NativeMIGraphXStatus), method.ReturnType);
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(bool) || parameter.ParameterType == typeof(string));
        }
    }
}
