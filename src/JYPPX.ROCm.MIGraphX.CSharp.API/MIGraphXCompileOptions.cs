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
        : this(nativeLibraryPath, offloadCopy, fastMath, exhaustiveTune, null, null)
    {
    }

    /// <summary>创建可选 2.18 编译设置；JSON 由原生解析器验证。 Creates optional 2.18 compilation settings; native code validates the JSON.</summary>
    /// <param name="nativeLibraryPath">原生库路径。 Native library path.</param>
    /// <param name="offloadCopy">插入主机复制。 Inserts host copies.</param>
    /// <param name="fastMath">近似数学开关。 Approximate math switch.</param>
    /// <param name="exhaustiveTune">穷举调优开关。 Exhaustive tuning switch.</param>
    /// <param name="compileMode">可选编译策略。 Optional compilation strategy.</param>
    /// <param name="backendOptionsJson">已物化的后端 JSON，无格式参数。 Materialized backend JSON without format arguments.</param>
    public MIGraphXCompileOptions(string nativeLibraryPath, bool offloadCopy, bool fastMath, bool exhaustiveTune, MIGraphXCompileMode? compileMode, string? backendOptionsJson)
    {
        if (compileMode.HasValue && !Enum.IsDefined(typeof(MIGraphXCompileMode), compileMode.Value)) throw new ArgumentOutOfRangeException(nameof(compileMode));
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        if (compileMode.HasValue || backendOptionsJson is not null) runtime.RequireFeature(MIGraphXFeature.AdvancedCompilation);
        var handle = NativeCompileOptionsHandle.Create(offloadCopy, fastMath, exhaustiveTune);
        try
        {
            if (compileMode.HasValue)
                NativeStatus.ThrowIfFailed(NativeMethods.CompileOptionsSetCompileMode(handle.DangerousGetHandle(), (sbyte)compileMode.Value), "migraphx_compile_options_set_compile_mode");
            if (backendOptionsJson is not null)
            {
                using var json = new StrictUtf8String(backendOptionsJson.Replace("%", "%%"), nameof(backendOptionsJson));
                NativeStatus.ThrowIfFailed(NativeMethods.CompileOptionsSetAdvanceBackendOptions(handle.DangerousGetHandle(), json.Pointer), "migraphx_compile_options_set_advance_backend_options");
            }
            owner = new NativeResourceOwner<NativeCompileOptionsHandle>(runtime, handle);
        }
        catch { handle.Dispose(); throw; }
        OffloadCopy = offloadCopy;
        FastMath = fastMath;
        ExhaustiveTune = exhaustiveTune;
        CompileMode = compileMode;
        BackendOptionsJson = backendOptionsJson;
    }

    private MIGraphXCompileOptions(NativeRuntime runtime, NativeCompileOptionsHandle handle, bool offloadCopy, bool fastMath, bool exhaustiveTune, MIGraphXCompileMode? compileMode, string? backendOptionsJson)
    {
        owner = new NativeResourceOwner<NativeCompileOptionsHandle>(runtime, handle);
        OffloadCopy = offloadCopy;
        FastMath = fastMath;
        ExhaustiveTune = exhaustiveTune;
        CompileMode = compileMode;
        BackendOptionsJson = backendOptionsJson;
    }

    /// <summary>获取构造时应用的 offload-copy 设置。 Gets the offload-copy setting applied at construction.</summary>
    public bool OffloadCopy { get; }

    /// <summary>获取是否启用近似 fast-math。 Gets whether approximate fast-math is enabled.</summary>
    public bool FastMath { get; }

    /// <summary>获取是否启用穷举 kernel tuning。 Gets whether exhaustive kernel tuning is enabled.</summary>
    public bool ExhaustiveTune { get; }

    /// <summary>显式编译策略，null 表示原生默认值。 Explicit compile strategy; null uses the native default.</summary>
    public MIGraphXCompileMode? CompileMode { get; }

    /// <summary>物化后端选项，null 表示原生默认值。 Materialized backend options; null uses native defaults.</summary>
    public string? BackendOptionsJson { get; }

    internal NativeResourceOwner<NativeCompileOptionsHandle> Owner => owner;

    /// <summary>使用 native assign-to 创建独立编译选项副本。 Creates an independent compile-options clone through native assign-to.</summary>
    public MIGraphXCompileOptions Clone()
        => owner.WithHandle(handle => new MIGraphXCompileOptions(owner.Runtime, NativeCompileOptionsHandle.CloneFrom(handle), OffloadCopy, FastMath, ExhaustiveTune, CompileMode, BackendOptionsJson));

    /// <summary>确定性释放 owned options handle；重复调用安全。 Deterministically releases the owned options handle; repeated calls are safe.</summary>
    public void Dispose() => owner.Dispose();
}
