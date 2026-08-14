using System;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示拥有默认静态 ONNX 解析 options handle 的资源安全对象。
/// Represents a resource-safe object that owns a default static ONNX parsing-options handle.
/// </summary>
public sealed class MIGraphXOnnxOptions : IDisposable
{
    private readonly NativeResourceOwner<NativeOnnxOptionsHandle> owner;

    /// <summary>
    /// 使用显式原生库创建默认 ONNX 解析选项。
    /// Creates default ONNX parsing options using an explicit native library.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    public MIGraphXOnnxOptions(string nativeLibraryPath)
    {
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        owner = new NativeResourceOwner<NativeOnnxOptionsHandle>(runtime, NativeOnnxOptionsHandle.Create());
    }

    internal NativeResourceOwner<NativeOnnxOptionsHandle> Owner => owner;

    /// <summary>确定性释放 owned options handle；重复调用安全。 Deterministically releases the owned options handle; repeated calls are safe.</summary>
    public void Dispose() => owner.Dispose();
}
