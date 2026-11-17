using System;
using JYPPX.ROCm.MIGraphXSharp.Loading;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeRuntime
{
    private NativeRuntime(string path)
    {
        Path = path;
    }

    internal string Path { get; }

    internal void RequireFeature(MIGraphXFeature feature)
    {
        var missing = NativeLibraryLoader.MissingActiveExports(NativeFeatures.Exports(feature));
        if (missing.Length != 0)
            throw new NotSupportedException($"The loaded MIGraphX runtime '{Path}' does not support {feature}. Missing exports: {string.Join(", ", missing)}.");
    }

    internal static NativeRuntime LoadTensorFlow(string path)
    {
        var runtime = Load(path);
        runtime.RequireFeature(MIGraphXFeature.TensorFlow);
        return runtime;
    }

    internal static NativeRuntime Load(string nativeLibraryPath)
    {
        var result = NativeLibraryLoader.LoadExplicit(nativeLibraryPath, requireOnnxWorkflow: true, requireManagedObjects: true);
        if (!result.Success)
        {
            throw new MIGraphXNativeLoadException(result.Diagnostics);
        }

        return new NativeRuntime(System.IO.Path.GetFullPath(result.LoadedPath!));
    }

    internal static NativeRuntime LoadDynamicShapes(string nativeLibraryPath)
    {
        var result = NativeLibraryLoader.LoadExplicit(nativeLibraryPath, requireOnnxWorkflow: true, requireManagedObjects: true, requireDynamicShapes: true);
        if (!result.Success)
        {
            throw new MIGraphXNativeLoadException(result.Diagnostics);
        }
        return new NativeRuntime(System.IO.Path.GetFullPath(result.LoadedPath!));
    }

    internal static NativeRuntime LoadRegistry(string nativeLibraryPath)
    {
        var result = NativeLibraryLoader.LoadExplicit(nativeLibraryPath, requireOnnxWorkflow: true, requireRegistry: true);
        if (!result.Success)
        {
            throw new MIGraphXNativeLoadException(result.Diagnostics);
        }
        return new NativeRuntime(System.IO.Path.GetFullPath(result.LoadedPath!));
    }

    internal void RequireSame(NativeRuntime other, string parameterName)
    {
        if (!NativeLibraryLoader.PathsEqual(Path, other.Path))
        {
            throw new ArgumentException("The native resource belongs to a different loaded MIGraphX library.", parameterName);
        }
    }

    internal void RequireAsync()
    {
        var result = NativeLibraryLoader.LoadExplicit(Path, requireOnnxWorkflow: true, requireManagedObjects: true, requireAsync: true);
        if (!result.Success) throw new MIGraphXNativeLoadException(result.Diagnostics);
    }

    internal void RequireComparison()
    {
        var result = NativeLibraryLoader.LoadExplicit(Path, requireOnnxWorkflow: true, requireManagedObjects: true, requireComparison: true);
        if (!result.Success) throw new MIGraphXNativeLoadException(result.Diagnostics);
    }

    internal void RequireOperationCreate()
    {
        var result = NativeLibraryLoader.LoadExplicit(
            Path,
            requireOnnxWorkflow: true,
            requireManagedObjects: true,
            requireOperationCreate: true);
        if (!result.Success) throw new MIGraphXNativeLoadException(result.Diagnostics);
    }
}
