using System;
using System.Collections.Generic;
using JYPPX.ROCm.MIGraphXSharp.Diagnostics;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示 MIGraphX 原生库或所需 frontend 导出无法加载，并保留结构化诊断。
/// Represents a failure to load the MIGraphX native library or required frontend exports and preserves structured diagnostics.
/// </summary>
public sealed class MIGraphXNativeLoadException : Exception
{
    internal MIGraphXNativeLoadException(IReadOnlyList<MIGraphXNativeDiagnostic> diagnostics)
        : base(diagnostics.Count == 0 ? "MIGraphX native loading failed without diagnostics." : diagnostics[diagnostics.Count - 1].Message)
    {
        Diagnostics = new List<MIGraphXNativeDiagnostic>(diagnostics).AsReadOnly();
    }

    /// <summary>
    /// 获取按候选顺序排列且保留原始平台错误的诊断。
    /// Gets candidate-ordered diagnostics that preserve original platform errors.
    /// </summary>
    public IReadOnlyList<MIGraphXNativeDiagnostic> Diagnostics { get; }
}
