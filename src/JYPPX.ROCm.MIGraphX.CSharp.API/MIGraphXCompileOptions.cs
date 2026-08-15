using System;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示拥有 MIGraphX 同步编译 options handle 的资源安全对象。
/// Represents a resource-safe object that owns a MIGraphX synchronous compilation-options handle.
/// </summary>
public sealed class MIGraphXCompileOptions : IDisposable
{
    private readonly NativeResourceOwner<NativeCompileOptionsHandle> owner;

    /// <summary>
    /// 使用显式原生库和固定 offload-copy 设置创建编译选项。
    /// Creates compilation options with an explicit native library and fixed offload-copy setting.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="offloadCopy">是否让 MIGraphX 插入 host/device copy。 Whether MIGraphX inserts host/device copies.</param>
    public MIGraphXCompileOptions(string nativeLibraryPath, bool offloadCopy = true)
        : this(nativeLibraryPath, offloadCopy, false, false)
    {
    }

    /// <summary>
    /// 使用显式 offload-copy、fast-math 和 exhaustive-tune 设置创建编译选项。
    /// Creates compilation options with explicit offload-copy, fast-math, and exhaustive-tune settings.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="offloadCopy">是否让 MIGraphX 插入 host/device copy。 Whether MIGraphX inserts host/device copies.</param>
    /// <param name="fastMath">是否允许更快的近似数学实现。 Whether faster approximate math implementations are allowed.</param>
    /// <param name="exhaustiveTune">是否穷举搜索最快 kernel。 Whether to exhaustively search for the fastest kernel.</param>
    public MIGraphXCompileOptions(string nativeLibraryPath, bool offloadCopy, bool fastMath, bool exhaustiveTune)
    {
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        owner = new NativeResourceOwner<NativeCompileOptionsHandle>(runtime, NativeCompileOptionsHandle.Create(offloadCopy, fastMath, exhaustiveTune));
        OffloadCopy = offloadCopy;
        FastMath = fastMath;
        ExhaustiveTune = exhaustiveTune;
    }

    /// <summary>获取构造时应用的 offload-copy 设置。 Gets the offload-copy setting applied at construction.</summary>
    public bool OffloadCopy { get; }

    /// <summary>获取是否启用近似 fast-math。 Gets whether approximate fast-math is enabled.</summary>
    public bool FastMath { get; }

    /// <summary>获取是否启用穷举 kernel tuning。 Gets whether exhaustive kernel tuning is enabled.</summary>
    public bool ExhaustiveTune { get; }

    internal NativeResourceOwner<NativeCompileOptionsHandle> Owner => owner;

    /// <summary>确定性释放 owned options handle；重复调用安全。 Deterministically releases the owned options handle; repeated calls are safe.</summary>
    public void Dispose() => owner.Dispose();
}
