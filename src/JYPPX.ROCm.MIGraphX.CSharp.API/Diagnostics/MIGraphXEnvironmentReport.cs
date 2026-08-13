using System.Collections.Generic;

namespace JYPPX.ROCm.MIGraphXSharp.Diagnostics;

/// <summary>
/// 表示显式 MIGraphX 原生探测结果，不推断 GPU 或 ONNX 能力。
/// Represents an explicit MIGraphX native probe result without inferring GPU or ONNX capability.
/// </summary>
public sealed class MIGraphXEnvironmentReport
{
    internal MIGraphXEnvironmentReport(string state, string? loadedPath, bool exportsComplete, bool objectsExecuted, IReadOnlyList<MIGraphXNativeDiagnostic> diagnostics)
    {
        State = state;
        LoadedPath = loadedPath;
        ExportsComplete = exportsComplete;
        ObjectsExecuted = objectsExecuted;
        Diagnostics = diagnostics;
    }

    /// <summary>获取 `not-available`、`loaded`、`executed` 或 `failed` 状态。 Gets the `not-available`, `loaded`, `executed`, or `failed` state.</summary>
    public string State { get; }

    /// <summary>获取已加载路径；未加载时为 <see langword="null"/>。 Gets the loaded path, or <see langword="null"/> when no library loaded.</summary>
    public string? LoadedPath { get; }

    /// <summary>获取固定 M1 导出是否完整。 Gets whether the frozen M1 exports were complete.</summary>
    public bool ExportsComplete { get; }

    /// <summary>获取 target/program create/assign/destroy 是否实际调用。 Gets whether target/program create/assign/destroy were actually called.</summary>
    public bool ObjectsExecuted { get; }

    /// <summary>获取按顺序记录且保留平台错误的诊断。 Gets ordered diagnostics that preserve platform errors.</summary>
    public IReadOnlyList<MIGraphXNativeDiagnostic> Diagnostics { get; }
}
