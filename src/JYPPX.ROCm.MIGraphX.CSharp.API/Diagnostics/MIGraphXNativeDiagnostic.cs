namespace JYPPX.ROCm.MIGraphXSharp.Diagnostics;

/// <summary>
/// 表示原生加载或导出探测的分类。
/// Represents a classification from native loading or export probing.
/// </summary>
public enum MIGraphXNativeDiagnosticKind
{
    /// <summary>候选已记录但未加载。 The candidate was recorded without loading.</summary>
    Candidate = 0,
    /// <summary>调用者路径下没有文件。 No file existed at the caller path.</summary>
    FileNotFound = 1,
    /// <summary>文件位数或二进制格式不匹配。 File architecture or binary format was incompatible.</summary>
    BadImage = 2,
    /// <summary>文件存在但一个原生依赖无法解析。 The file existed but a native dependency could not be resolved.</summary>
    DependencyMissing = 3,
    /// <summary>平台加载器报告了其他错误。 The platform loader reported another error.</summary>
    LoadFailure = 4,
    /// <summary>固定 M1 入口未导出。 A frozen M1 entry point was not exported.</summary>
    ExportMissing = 5,
    /// <summary>库和固定 M1 导出已加载。 The library and frozen M1 exports were loaded.</summary>
    Loaded = 6,
    /// <summary>target/program 纵向路径已执行。 The target/program vertical path was executed.</summary>
    Executed = 7,
}

/// <summary>
/// 描述一次原生候选或加载结果；消息保留平台错误文本。
/// Describes one native candidate or load result; the message preserves platform error text.
/// </summary>
public sealed class MIGraphXNativeDiagnostic
{
    internal MIGraphXNativeDiagnostic(string candidate, string source, bool? fileExists, MIGraphXNativeDiagnosticKind kind, string message)
    {
        Candidate = candidate;
        Source = source;
        FileExists = fileExists;
        Kind = kind;
        Message = message;
    }

    /// <summary>获取被检查的候选名称或路径。 Gets the candidate name or path that was inspected.</summary>
    public string Candidate { get; }

    /// <summary>获取候选来源。 Gets the candidate source.</summary>
    public string Source { get; }

    /// <summary>获取文件存在性；系统解析候选为 <see langword="null"/>。 Gets file existence, or <see langword="null"/> for a system-resolved candidate.</summary>
    public bool? FileExists { get; }

    /// <summary>获取分类。 Gets the classification.</summary>
    public MIGraphXNativeDiagnosticKind Kind { get; }

    /// <summary>获取包含原始平台错误的诊断消息。 Gets the diagnostic message including the original platform error.</summary>
    public string Message { get; }
}
