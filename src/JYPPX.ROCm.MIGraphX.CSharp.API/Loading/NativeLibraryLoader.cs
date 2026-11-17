using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Diagnostics;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp.Loading;

internal sealed class NativeLoadResult
{
    internal NativeLoadResult(bool success, string? loadedPath, List<MIGraphXNativeDiagnostic> diagnostics)
    {
        Success = success;
        LoadedPath = loadedPath;
        Diagnostics = diagnostics;
    }

    internal bool Success { get; }

    internal string? LoadedPath { get; }

    internal List<MIGraphXNativeDiagnostic> Diagnostics { get; }
}

internal static class NativeLibraryLoader
{
    internal const string LogicalName = "migraphx_c";
    private static readonly object Sync = new object();
    private static IntPtr loadedHandle;
    private static string? loadedPath;
    // The C API and its target registries live for the process lifetime. Keep
    // matching plugin handles alive for the same period, without changing PATH.
    private static readonly Dictionary<string, IntPtr> targetPlugins = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
#if MIGRAPHX_NATIVE_LIBRARY_PATH
    private static int resolverConfigured;
#endif

#if !MIGRAPHX_NATIVE_LIBRARY_PATH
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);
#endif

    internal static void LoadTargetPlugin(string targetName)
        => LoadTargetPlugin(targetName, required: true);

    internal static void LoadSerializationPlugins()
    {
        // Deserialization discovers the serialized target inside native code.
        // Make sibling registries available first. A host-only reference file
        // must remain loadable even when optional GPU dependencies are absent.
        foreach (var name in new[] { "ref", "gpu", "cpu" })
            LoadTargetPlugin(name, required: false);
    }

    private static void LoadTargetPlugin(string targetName, bool required)
    {
        if (!IsWindows()) return;
        var fileName = targetName == "ref" ? "migraphx_ref.dll"
            : targetName == "gpu" ? "migraphx_gpu.dll"
            : targetName == "cpu" ? "migraphx_cpu.dll" : null;
        if (fileName is null) return;
        lock (Sync)
        {
            if (loadedHandle == IntPtr.Zero || loadedPath is null || !Path.IsPathRooted(loadedPath)) return;
            var plugin = Path.Combine(Path.GetDirectoryName(loadedPath)!, fileName);
            // Older/system runtimes can provide plugins through their own loader.
            if (targetPlugins.ContainsKey(plugin) || !File.Exists(plugin)) return;
            if (!TryLoad(plugin, out var handle, out var failure))
            {
                if (!required) return;
                throw new MIGraphXNativeLoadException(new[] {
                    new MIGraphXNativeDiagnostic(plugin, "selected-runtime-target-plugin", true,
                        MIGraphXNativeDiagnosticKind.LoadFailure, failure ?? "Target plugin loading failed.") });
            }
            targetPlugins.Add(plugin, handle);
        }
    }

    internal static NativeLoadResult LoadExplicit(
        string path,
        bool requireOnnxWorkflow = false,
        bool requireManagedObjects = false,
        bool requireDynamicShapes = false,
        bool requireAsync = false,
        bool requireRegistry = false,
        bool requireComparison = false,
        bool requireOperationCreate = false)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }
        if (!IsAbsoluteFilePath(path))
        {
            throw new ArgumentException("The native library path must be absolute.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var diagnostics = new List<MIGraphXNativeDiagnostic>();
        if (!File.Exists(fullPath))
        {
            diagnostics.Add(new MIGraphXNativeDiagnostic(fullPath, "explicit-path", false, MIGraphXNativeDiagnosticKind.FileNotFound, "The caller-supplied native library file does not exist."));
            return new NativeLoadResult(false, null, diagnostics);
        }

        return LoadCandidate(fullPath, "explicit-path", true, diagnostics, requireOnnxWorkflow, requireManagedObjects, requireDynamicShapes, requireAsync, requireRegistry, requireComparison, requireOperationCreate);
    }

    internal static NativeLoadResult LoadSystemCandidates()
    {
        var diagnostics = new List<MIGraphXNativeDiagnostic>();
        foreach (var candidate in EnumerateCandidates())
        {
            if (candidate.IsPath && !File.Exists(candidate.Value))
            {
                diagnostics.Add(new MIGraphXNativeDiagnostic(candidate.Value, candidate.Source, false, MIGraphXNativeDiagnosticKind.FileNotFound, "Candidate file does not exist."));
                continue;
            }

            var result = LoadCandidate(candidate.Value, candidate.Source, candidate.IsPath, diagnostics, false, false);
            if (result.Success)
            {
                return result;
            }
        }

        return new NativeLoadResult(false, null, diagnostics);
    }

    internal static IReadOnlyList<string> CandidateOrderForCurrentPlatform() => EnumerateCandidates()
        .Select(candidate => $"{candidate.Source}:{candidate.Value}")
        .ToArray();

    private static NativeLoadResult LoadCandidate(
        string candidate,
        string source,
        bool isFilePath,
        List<MIGraphXNativeDiagnostic> diagnostics,
        bool requireOnnxWorkflow,
        bool requireManagedObjects,
        bool requireDynamicShapes = false,
        bool requireAsync = false,
        bool requireRegistry = false,
        bool requireComparison = false,
        bool requireOperationCreate = false)
    {
        lock (Sync)
        {
            if (loadedHandle != IntPtr.Zero)
            {
                if (PathsEqual(loadedPath, candidate))
                {
                    var missingFromActive = MissingExports(loadedHandle, RequiredExports(requireOnnxWorkflow, requireManagedObjects, requireDynamicShapes, requireAsync, requireRegistry, requireComparison, requireOperationCreate));
                    if (missingFromActive.Length != 0)
                    {
                        diagnostics.Add(CreateMissingExportDiagnostic(
                            candidate,
                            source,
                            isFilePath,
                            missingFromActive,
                            requireOnnxWorkflow,
                            requireManagedObjects,
                            requireRegistry,
                            requireComparison,
                            requireOperationCreate));
                        return new NativeLoadResult(false, null, diagnostics);
                    }

                    diagnostics.Add(new MIGraphXNativeDiagnostic(candidate, source, isFilePath ? true : null, MIGraphXNativeDiagnosticKind.Loaded, "The same native library is already loaded and was reused."));
                    return new NativeLoadResult(true, loadedPath, diagnostics);
                }

                diagnostics.Add(new MIGraphXNativeDiagnostic(candidate, source, isFilePath ? true : null, MIGraphXNativeDiagnosticKind.LoadFailure, $"A different native library is already active: {loadedPath}"));
                return new NativeLoadResult(false, null, diagnostics);
            }

            IntPtr handle;
            string? failure;
            if (!TryLoad(candidate, out handle, out failure))
            {
                diagnostics.Add(new MIGraphXNativeDiagnostic(candidate, source, isFilePath ? true : null, ClassifyLoadFailure(failure!), failure!));
                return new NativeLoadResult(false, null, diagnostics);
            }

            var requiredExports = RequiredExports(requireOnnxWorkflow, requireManagedObjects, requireDynamicShapes, requireAsync, requireRegistry, requireComparison, requireOperationCreate);
            var missing = MissingExports(handle, requiredExports);
            if (missing.Length != 0)
            {
                Free(handle);
                diagnostics.Add(CreateMissingExportDiagnostic(
                    candidate,
                    source,
                    isFilePath,
                    missing,
                    requireOnnxWorkflow,
                    requireManagedObjects,
                    requireRegistry,
                    requireComparison,
                    requireOperationCreate));
                return new NativeLoadResult(false, null, diagnostics);
            }

            loadedHandle = handle;
            loadedPath = candidate;
#if MIGRAPHX_NATIVE_LIBRARY_PATH
            try
            {
                ConfigureResolver();
            }
            catch (InvalidOperationException exception)
            {
                loadedHandle = IntPtr.Zero;
                loadedPath = null;
                Free(handle);
                diagnostics.Add(new MIGraphXNativeDiagnostic(candidate, source, isFilePath ? true : null, MIGraphXNativeDiagnosticKind.LoadFailure, $"Loaded the native library, but could not install the assembly resolver: {exception.Message}"));
                return new NativeLoadResult(false, null, diagnostics);
            }
#endif
            var loadedMessage = requireOperationCreate
                ? "Loaded native library and verified the constrained operation-create export."
                : requireComparison
                ? "Loaded native library and verified the fixed Content equality exports."
                : requireRegistry
                    ? "Loaded native library and verified the fixed ONNX registry exports."
                    : requireManagedObjects
                ? "Loaded native library and verified the managed-object exports."
                : requireOnnxWorkflow
                    ? "Loaded native library and verified the fixed ONNX synchronous-workflow exports."
                    : "Loaded native library and verified all required environment exports.";
            diagnostics.Add(new MIGraphXNativeDiagnostic(candidate, source, isFilePath ? true : null, MIGraphXNativeDiagnosticKind.Loaded, loadedMessage));
            return new NativeLoadResult(true, loadedPath, diagnostics);
        }
    }

    private static IEnumerable<string> RequiredExports(
        bool requireOnnxWorkflow,
        bool requireManagedObjects,
        bool requireDynamicShapes = false,
        bool requireAsync = false,
        bool requireRegistry = false,
        bool requireComparison = false,
        bool requireOperationCreate = false)
    {
        IEnumerable<string> required;
        if (requireAsync)
        {
            required = NativeMethods.OnnxRequiredExports.Concat(NativeManagedMethods.AdditionalRequiredExports).Concat(NativeAsyncMethods.AdditionalRequiredExports);
        }
        else if (requireDynamicShapes)
        {
            required = NativeMethods.OnnxRequiredExports.Concat(NativeManagedMethods.AdditionalRequiredExports).Concat(NativeDynamicMethods.AdditionalRequiredExports);
        }
        else if (requireManagedObjects)
        {
            required = NativeMethods.OnnxRequiredExports.Concat(NativeManagedMethods.AdditionalRequiredExports);
        }
        else
        {
            required = requireOnnxWorkflow ? NativeMethods.OnnxRequiredExports : NativeMethods.EnvironmentRequiredExports;
        }
        if (requireRegistry) required = required.Concat(NativeComparisonMethods.RegistryRequiredExports);
        if (requireComparison) required = required.Concat(NativeComparisonMethods.EqualityRequiredExports);
        if (requireOperationCreate) required = required.Concat(new[] { "migraphx_operation_create" });
        return required.Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<NativeCandidate> EnumerateCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var rid = GetRuntimeIdentifier();
        var names = PlatformFileNames();
        foreach (var name in names)
        {
            yield return new NativeCandidate(Path.Combine(baseDirectory, "runtimes", rid, "native", name), "application-rid-native", true);
        }
        foreach (var name in names)
        {
            yield return new NativeCandidate(Path.Combine(baseDirectory, name), "application-base", true);
        }
        foreach (var name in SystemNames())
        {
            yield return new NativeCandidate(name, "system-loader", false);
        }
    }

    private static IReadOnlyList<string> PlatformFileNames()
    {
        if (IsLinux())
        {
            return new[] { "libmigraphx_c.so.3", "libmigraphx_c.so" };
        }
        if (IsWindows())
        {
            return new[] { "migraphx_c.dll" };
        }
        if (IsMacOS())
        {
            return new[] { "libmigraphx_c.dylib" };
        }
        return new[] { LogicalName };
    }

    private static IReadOnlyList<string> SystemNames()
    {
        if (IsLinux())
        {
            return new[] { "libmigraphx_c.so.3", LogicalName };
        }
        if (IsWindows())
        {
            return new[] { "migraphx_c.dll", LogicalName };
        }
        if (IsMacOS())
        {
            return new[] { "libmigraphx_c.dylib", LogicalName };
        }
        return new[] { LogicalName };
    }

    private static string GetRuntimeIdentifier()
    {
        if (IsWindows())
        {
            return Environment.Is64BitProcess ? "win-x64" : "win-x86";
        }
        if (IsLinux())
        {
            return Environment.Is64BitProcess ? "linux-x64" : "linux-x86";
        }
        if (IsMacOS())
        {
            return Environment.Is64BitProcess ? "osx-x64" : "osx-x86";
        }
        return "unknown";
    }

    private static bool IsWindows()
    {
        var platform = Environment.OSVersion.Platform;
        return platform == PlatformID.Win32NT || platform == PlatformID.Win32Windows || platform == PlatformID.Win32S || platform == PlatformID.WinCE;
    }

    private static bool IsMacOS()
    {
        return Environment.OSVersion.Platform == PlatformID.MacOSX
            || Environment.OSVersion.Platform == PlatformID.Unix
            && Directory.Exists("/System/Library/CoreServices");
    }

    private static bool IsLinux() => Environment.OSVersion.Platform == PlatformID.Unix && !IsMacOS();

    internal static bool PathsEqual(string? left, string right) => string.Equals(
        left,
        right,
        IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsAbsoluteFilePath(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return false;
        }

        return Path.IsPathRooted(path);
    }

    internal static MIGraphXNativeDiagnosticKind ClassifyLoadFailure(string message)
    {
        var text = message.ToLowerInvariant();
        if (text.Contains("badimageformatexception") || text.Contains("0x8007000b") || text.Contains("win32 error 193") || text.Contains("bad image") || text.Contains("wrong elf class") || text.Contains("invalid elf header") || text.Contains("file too short") || text.Contains("not a valid win32") || text.Contains("architecture"))
        {
            return MIGraphXNativeDiagnosticKind.BadImage;
        }
        if (text.Contains("dllnotfoundexception") || text.Contains("0x8007007e") || text.Contains("win32 error 126") || text.Contains("dependent") || text.Contains("dependency") || text.Contains("cannot open shared object") || text.Contains("module could not be found"))
        {
            return MIGraphXNativeDiagnosticKind.DependencyMissing;
        }
        return MIGraphXNativeDiagnosticKind.LoadFailure;
    }

    private static string[] MissingExports(IntPtr handle, IEnumerable<string> requiredExports) => requiredExports
        .Where(exportName => !TryGetExport(handle, exportName))
        .ToArray();

    internal static string[] MissingActiveExports(IEnumerable<string> exports)
    {
        lock (Sync)
        {
            if (loadedHandle == IntPtr.Zero) throw new InvalidOperationException("Load a MIGraphX runtime before querying capabilities.");
            return MissingExports(loadedHandle, exports);
        }
    }

    private static MIGraphXNativeDiagnostic CreateMissingExportDiagnostic(
        string candidate,
        string source,
        bool isFilePath,
        string[] missing,
        bool requireOnnxWorkflow,
        bool requireManagedObjects,
        bool requireRegistry,
        bool requireComparison,
        bool requireOperationCreate)
    {
        var kind = requireRegistry || requireComparison || requireManagedObjects
            ? MIGraphXNativeDiagnosticKind.ExportMissing
            : requireOnnxWorkflow
                ? MIGraphXNativeDiagnosticKind.OnnxFrontendMissing
                : MIGraphXNativeDiagnosticKind.ExportMissing;
        var scope = requireOperationCreate
            ? "constrained operation-create"
            : requireComparison
            ? "Content equality"
            : requireRegistry
                ? "ONNX registry"
                : requireManagedObjects
                    ? "managed-object"
                    : requireOnnxWorkflow
                        ? "ONNX synchronous-workflow"
                        : "environment";
        return new MIGraphXNativeDiagnostic(candidate, source, isFilePath ? true : null, kind, $"The native library loaded but is missing required {scope} exports: {string.Join(", ", missing)}.");
    }

#if MIGRAPHX_NATIVE_LIBRARY_PATH
    private static bool TryLoad(string candidate, out IntPtr handle, out string? failure)
    {
        try
        {
            handle = NativeLibrary.Load(candidate);
            failure = null;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException || exception is BadImageFormatException || exception is FileLoadException)
        {
            handle = IntPtr.Zero;
            failure = $"{exception.GetType().Name} (HRESULT 0x{exception.HResult:X8}): {exception.Message}";
            return false;
        }
    }

    private static bool TryGetExport(IntPtr handle, string exportName) => NativeLibrary.TryGetExport(handle, exportName, out _);

    private static void Free(IntPtr handle) => NativeLibrary.Free(handle);

    private static void ConfigureResolver()
    {
        if (resolverConfigured != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveLoadedLibrary);
        resolverConfigured = 1;
    }

    private static IntPtr ResolveLoadedLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return string.Equals(libraryName, LogicalName, StringComparison.Ordinal) ? loadedHandle : IntPtr.Zero;
    }
#else
    private static bool TryLoad(string candidate, out IntPtr handle, out string? failure)
    {
        if (IsWindows())
        {
            handle = LoadLibraryExW(candidate, IntPtr.Zero, Path.IsPathRooted(candidate) ? 0x00000008u : 0u);
            if (handle != IntPtr.Zero)
            {
                failure = null;
                return true;
            }

            failure = $"LoadLibraryExW failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }

        handle = IntPtr.Zero;
        failure = "Explicit native loading is not implemented for this legacy target and platform. Use a supported modern .NET target or Windows .NET Framework.";
        return false;
    }

    private static bool TryGetExport(IntPtr handle, string exportName) => IsWindows() && GetProcAddress(handle, exportName) != IntPtr.Zero;

    private static void Free(IntPtr handle)
    {
        if (IsWindows() && handle != IntPtr.Zero)
        {
            FreeLibrary(handle);
        }
    }
#endif

    private sealed class NativeCandidate
    {
        internal NativeCandidate(string value, string source, bool isPath)
        {
            Value = value;
            Source = source;
            IsPath = isPath;
        }

        internal string Value { get; }

        internal string Source { get; }

        internal bool IsPath { get; }

    }
}
