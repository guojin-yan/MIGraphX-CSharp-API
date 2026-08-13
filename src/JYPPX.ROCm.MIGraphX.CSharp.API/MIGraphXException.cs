using System;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示 MIGraphX C API 返回的失败状态，并保留原始整数与调用上下文。
/// Represents a failure status returned by the MIGraphX C API and preserves the raw integer and call context.
/// </summary>
public sealed class MIGraphXException : Exception
{
    /// <summary>
    /// 使用原生状态和操作上下文初始化异常。
    /// Initializes the exception with a native status and operation context.
    /// </summary>
    /// <param name="statusCode">原始原生状态整数。 The raw native status integer.</param>
    /// <param name="operation">失败的 C API 入口名称。 The C API entry point that failed.</param>
    internal MIGraphXException(int statusCode, string operation)
        : base(CreateMessage(statusCode, operation))
    {
        StatusCode = statusCode;
        Operation = operation;
    }

    /// <summary>
    /// 获取未经截断或替换的原始原生状态整数。
    /// Gets the raw native status integer without truncation or substitution.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// 获取失败的原生入口名称。
    /// Gets the native entry-point name that failed.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// 获取已知状态；未知整数返回 <see langword="null"/>。
    /// Gets the known status, or <see langword="null"/> for an unknown integer.
    /// </summary>
    public MIGraphXStatus? KnownStatus => Enum.IsDefined(typeof(MIGraphXStatus), StatusCode)
        ? (MIGraphXStatus)StatusCode
        : null;

    private static string CreateMessage(int statusCode, string operation)
    {
        var name = Enum.IsDefined(typeof(MIGraphXStatus), statusCode)
            ? ((MIGraphXStatus)statusCode).ToString()
            : "UnknownStatus";
        return $"MIGraphX call '{operation}' failed with status {statusCode} ({name}). The frozen C API exposes no last-error text function.";
    }
}
