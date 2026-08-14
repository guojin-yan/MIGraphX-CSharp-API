using System;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示拥有 MIGraphX 编译目标 handle 的资源安全对象。
/// Represents a resource-safe object that owns a MIGraphX compilation-target handle.
/// </summary>
public sealed class MIGraphXTarget : IDisposable
{
    private readonly NativeResourceOwner<NativeTargetHandle> owner;

    /// <summary>
    /// 从显式原生库和严格 UTF-8 目标名称创建目标。
    /// Creates a target from an explicit native library and strict UTF-8 target name.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="name">目标名称，例如 <c>gpu</c>。 Target name, such as <c>gpu</c>.</param>
    public MIGraphXTarget(string nativeLibraryPath, string name = "gpu")
    {
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        owner = new NativeResourceOwner<NativeTargetHandle>(runtime, NativeTargetHandle.Create(name));
        Name = name;
    }

    /// <summary>获取创建时使用的目标名称。 Gets the target name used at creation.</summary>
    public string Name { get; }

    internal NativeResourceOwner<NativeTargetHandle> Owner => owner;

    /// <summary>确定性释放 owned target handle；重复调用安全。 Deterministically releases the owned target handle; repeated calls are safe.</summary>
    public void Dispose() => owner.Dispose();
}
