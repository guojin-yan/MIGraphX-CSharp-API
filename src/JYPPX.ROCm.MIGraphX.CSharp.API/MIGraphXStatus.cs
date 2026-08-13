namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示冻结 MIGraphX C 头文件定义的状态码。
/// Represents a status code defined by the frozen MIGraphX C header.
/// </summary>
public enum MIGraphXStatus
{
    /// <summary>
    /// 调用成功。
    /// The call succeeded.
    /// </summary>
    Success = 0,

    /// <summary>
    /// 一个参数无效。
    /// A parameter was invalid.
    /// </summary>
    BadParameter = 1,

    /// <summary>
    /// 请求的目标未知。
    /// The requested target was unknown.
    /// </summary>
    UnknownTarget = 3,

    /// <summary>
    /// 原生实现报告了未分类错误。
    /// The native implementation reported an unclassified error.
    /// </summary>
    UnknownError = 4,
}
