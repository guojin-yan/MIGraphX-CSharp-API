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

    internal static NativeRuntime Load(string nativeLibraryPath)
    {
        var result = NativeLibraryLoader.LoadExplicit(nativeLibraryPath, requireOnnxWorkflow: true, requireManagedObjects: true);
        if (!result.Success)
        {
            throw new MIGraphXNativeLoadException(result.Diagnostics);
        }

        return new NativeRuntime(System.IO.Path.GetFullPath(result.LoadedPath!));
    }

    internal static NativeRuntime LoadM5(string nativeLibraryPath)
    {
        var result = NativeLibraryLoader.LoadExplicit(nativeLibraryPath, requireOnnxWorkflow: true, requireManagedObjects: true, requireM5: true);
        if (!result.Success)
        {
            throw new MIGraphXNativeLoadException(result.Diagnostics);
        }
        return new NativeRuntime(System.IO.Path.GetFullPath(result.LoadedPath!));
    }

    internal void RequireSame(NativeRuntime other, string parameterName)
    {
        if (!string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The native resource belongs to a different loaded MIGraphX library.", parameterName);
        }
    }
}
