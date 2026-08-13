namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 提供当前托管程序集的只读工程状态信息。
/// Provides read-only engineering status information for the current managed assembly.
/// </summary>
/// <remarks>
/// 此类型不探测或加载 MIGraphX 原生库，也不表示 GPU 或 ONNX 能力可用；请使用 <see cref="MIGraphXEnvironment"/> 进行显式探测。
/// This type neither probes nor loads the native MIGraphX library and does not indicate GPU or ONNX capability; use <see cref="MIGraphXEnvironment"/> for explicit probing.
/// </remarks>
public static class MIGraphXBuildInfo
{
    /// <summary>
    /// 获取当前本地工程候选包版本。
    /// Gets the current local engineering candidate package version.
    /// </summary>
    public const string PackageVersion = "0.0.0";

    /// <summary>
    /// 获取一个值，指示当前程序集是否包含固定 M1 Direct P/Invoke 声明；这不表示官方 MIGraphX 或 GPU runtime 已执行。
    /// Gets a value indicating whether this assembly contains frozen M1 Direct P/Invoke declarations; this does not indicate official MIGraphX or GPU runtime execution.
    /// </summary>
    public const bool NativeBindingsAvailable = true;
}
