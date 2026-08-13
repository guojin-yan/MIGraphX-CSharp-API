using JYPPX.ROCm.MIGraphXSharp.Diagnostics;
using JYPPX.ROCm.MIGraphXSharp.Interop;
using JYPPX.ROCm.MIGraphXSharp.Loading;
using System.Linq;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 提供显式、无下载且不修改进程搜索路径的 MIGraphX M1 环境探测。
/// Provides explicit MIGraphX M1 environment probing without downloads or process search-path mutation.
/// </summary>
public static class MIGraphXEnvironment
{
    /// <summary>
    /// 按可审计顺序检查应用 RID 目录、应用目录和系统 loader 候选，并可选择执行 target/program 生命周期。
    /// Checks application RID, application-directory, and system-loader candidates in auditable order and optionally exercises target/program lifetimes.
    /// </summary>
    /// <param name="exerciseObjects">是否执行 target/program create/assign/destroy。 Whether to execute target/program create/assign/destroy.</param>
    /// <param name="targetName">传给 `migraphx_target_create` 的严格 UTF-8 目标名。 The strict UTF-8 target name passed to `migraphx_target_create`.</param>
    /// <returns>包含每个候选与原始平台错误的结构化报告。 A structured report containing every candidate and original platform errors.</returns>
    /// <exception cref="System.ArgumentException">目标名为空、含 NUL 或无效 UTF-16。 The target name is empty, contains NUL, or has invalid UTF-16.</exception>
    /// <exception cref="MIGraphXException">原生入口返回非成功状态，包括未知整数。 A native entry point returns a non-success status, including an unknown integer.</exception>
    public static MIGraphXEnvironmentReport ProbeSystem(bool exerciseObjects = true, string targetName = "gpu")
    {
        return CreateReport(NativeLibraryLoader.LoadSystemCandidates(), exerciseObjects, targetName);
    }

    /// <summary>
    /// 从调用者给出的绝对文件路径加载原生库并检查固定 M1 导出，可选择执行 target/program 生命周期。
    /// Loads a native library from a caller-supplied absolute file path, checks the frozen M1 exports, and optionally exercises target/program lifetimes.
    /// </summary>
    /// <param name="nativeLibraryPath">原生库绝对文件路径；相对路径会被拒绝。 An absolute native-library file path; relative paths are rejected.</param>
    /// <param name="exerciseObjects">是否执行 target/program create/assign/destroy。 Whether to execute target/program create/assign/destroy.</param>
    /// <param name="targetName">传给 `migraphx_target_create` 的严格 UTF-8 目标名。 The strict UTF-8 target name passed to `migraphx_target_create`.</param>
    /// <returns>结构化加载、导出和执行诊断。 Structured loading, export, and execution diagnostics.</returns>
    /// <exception cref="System.ArgumentException">路径不是绝对路径，或目标名为空、含 NUL 或无效 UTF-16。 The path is not absolute, or the target name is empty, contains NUL, or has invalid UTF-16.</exception>
    /// <exception cref="MIGraphXException">原生入口返回非成功状态，包括未知整数。 A native entry point returns a non-success status, including an unknown integer.</exception>
    public static MIGraphXEnvironmentReport Probe(string nativeLibraryPath, bool exerciseObjects = true, string targetName = "gpu")
    {
        return CreateReport(NativeLibraryLoader.LoadExplicit(nativeLibraryPath), exerciseObjects, targetName);
    }

    private static MIGraphXEnvironmentReport CreateReport(NativeLoadResult result, bool exerciseObjects, string targetName)
    {
        if (!result.Success)
        {
            return new MIGraphXEnvironmentReport("not-available", null, false, false, result.Diagnostics);
        }

        if (!exerciseObjects)
        {
            return new MIGraphXEnvironmentReport("loaded", result.LoadedPath, true, false, result.Diagnostics);
        }

        NativeObjectProbe.Execute(targetName);
        var loadedDiagnostic = result.Diagnostics.Last(item => item.Kind == MIGraphXNativeDiagnosticKind.Loaded);
        result.Diagnostics.Add(new MIGraphXNativeDiagnostic(
            result.LoadedPath!,
            loadedDiagnostic.Source,
            loadedDiagnostic.FileExists,
            MIGraphXNativeDiagnosticKind.Executed,
            "Executed target/program create, assign_to, and destroy through the Direct P/Invoke declarations."));
        return new MIGraphXEnvironmentReport("executed", result.LoadedPath, true, true, result.Diagnostics);
    }
}
