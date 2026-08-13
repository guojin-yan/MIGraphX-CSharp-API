namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 提供当前托管程序集的只读工程状态信息。
/// Provides read-only engineering status information for the current managed assembly.
/// </summary>
/// <remarks>
/// 此类型不探测或加载 MIGraphX 原生库，也不表示 GPU 或 ONNX 能力可用。
/// This type neither probes nor loads the native MIGraphX library and does not indicate GPU or ONNX capability.
/// </remarks>
public static class MIGraphXBuildInfo
{
    /// <summary>
    /// 获取当前本地工程候选包版本。
    /// Gets the current local engineering candidate package version.
    /// </summary>
    public const string PackageVersion = "0.0.0";

    /// <summary>
    /// 获取一个值，指示当前程序集是否包含经过验证的 MIGraphX 原生绑定；M0 中始终为 <see langword="false"/>。
    /// Gets a value indicating whether this assembly contains verified native MIGraphX bindings; this is always <see langword="false"/> in M0.
    /// </summary>
    public const bool NativeBindingsAvailable = false;
}
