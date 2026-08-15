using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示拥有默认静态 ONNX 解析 options handle 的资源安全对象。
/// Represents a resource-safe object that owns a default static ONNX parsing-options handle.
/// </summary>
public sealed class MIGraphXOnnxOptions : IDisposable
{
    private readonly NativeResourceOwner<NativeOnnxOptionsHandle> owner;
    private readonly Dictionary<string, long[]> staticOverrides = new Dictionary<string, long[]>(StringComparer.Ordinal);
    private readonly Dictionary<string, MIGraphXDynamicDimension[]> dynamicOverrides = new Dictionary<string, MIGraphXDynamicDimension[]>(StringComparer.Ordinal);
    private long? defaultDimension;
    private MIGraphXDynamicDimension? defaultDynamicDimension;
    private long? defaultLoopIterations;
    private long? limitLoopIterations;
    private string? externalDataPath;

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

    /// <summary>设置输入参数的静态 shape override。 Sets a static shape override for an input parameter.</summary>
    /// <param name="name">输入参数名。 The input parameter name.</param>
    /// <param name="dimensions">静态维度。 The static dimensions.</param>
    public void SetInputParameterShape(string name, IReadOnlyList<long> dimensions)
    {
        if (dimensions is null) { throw new ArgumentNullException(nameof(dimensions)); }
        var copied = dimensions.ToArray();
        for (var index = 0; index < copied.Length; index++)
        {
            if (copied[index] <= 0) { throw new ArgumentOutOfRangeException(nameof(dimensions), "Static override dimensions must be positive."); }
            MIGraphXDynamicDimension.ValidateSizeT(copied[index], nameof(dimensions));
        }
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        using (var values = NativeSizeTArray.Alloc(copied, nameof(dimensions)))
        {
            owner.WithHandle(handle => NativeStatus.ThrowIfFailed(
                NativeMethods.OnnxOptionsSetInputParameterShape(handle, utf8.Pointer, values.Pointer, NativeSizeTArray.Count(copied.Length)),
                "migraphx_onnx_options_set_input_parameter_shape"));
        }
        lock (staticOverrides) { staticOverrides[name] = copied; dynamicOverrides.Remove(name); }
    }

    /// <summary>设置输入参数的动态 shape override。 Sets a dynamic shape override for an input parameter.</summary>
    /// <param name="name">输入参数名。 The input parameter name.</param>
    /// <param name="dimensions">动态维度范围。 The dynamic dimension ranges.</param>
    public void SetDynamicInputParameterShape(string name, IReadOnlyList<MIGraphXDynamicDimension> dimensions)
    {
        if (dimensions is null) { throw new ArgumentNullException(nameof(dimensions)); }
        var copied = dimensions.ToArray();
        if (copied.Any(value => value is null)) { throw new ArgumentException("Dynamic override dimensions must not contain null values.", nameof(dimensions)); }
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        using (var nativeDimensions = NativeDynamicDimensionsHandle.Create(copied))
        {
            owner.WithHandle(handle => NativeStatus.ThrowIfFailed(
                NativeMethods.OnnxOptionsSetDynInputParameterShape(handle, utf8.Pointer, nativeDimensions.DangerousGetHandle()),
                "migraphx_onnx_options_set_dyn_input_parameter_shape"));
        }
        lock (staticOverrides) { dynamicOverrides[name] = copied; staticOverrides.Remove(name); }
    }

    /// <summary>设置动态输入 shape 的兼容别名。 Compatibility alias for the dynamic input-shape setter.</summary>
    /// <param name="name">输入参数名。 The input parameter name.</param>
    /// <param name="dimensions">动态维度范围。 The dynamic dimension ranges.</param>
    public void SetDynInputParameterShape(string name, IReadOnlyList<MIGraphXDynamicDimension> dimensions) => SetDynamicInputParameterShape(name, dimensions);

    /// <summary>设置默认静态维度值。 Sets the default static dimension value.</summary>
    /// <param name="value">默认值。 The default value.</param>
    public void SetDefaultDimensionValue(long value)
    {
        MIGraphXDynamicDimension.ValidateSizeT(value, nameof(value));
        owner.WithHandle(handle => NativeStatus.ThrowIfFailed(
            NativeMethods.OnnxOptionsSetDefaultDimValue(handle, MIGraphXDynamicDimension.ToNativeSizeT(value, nameof(value))),
            "migraphx_onnx_options_set_default_dim_value"));
        lock (staticOverrides) { defaultDimension = value; defaultDynamicDimension = null; }
    }

    /// <summary>设置默认动态维度值。 Sets the default dynamic dimension value.</summary>
    /// <param name="value">默认范围。 The default range.</param>
    public void SetDefaultDynamicDimensionValue(MIGraphXDynamicDimension value)
    {
        if (value is null) { throw new ArgumentNullException(nameof(value)); }
        using (var native = NativeDynamicDimensionHandle.Create(value))
        {
            owner.WithHandle(handle => NativeStatus.ThrowIfFailed(
                NativeMethods.OnnxOptionsSetDefaultDynDimValue(handle, native.DangerousGetHandle()),
                "migraphx_onnx_options_set_default_dyn_dim_value"));
        }
        lock (staticOverrides) { defaultDynamicDimension = value; defaultDimension = null; }
    }

    /// <summary>设置默认动态维度的兼容别名。 Compatibility alias for the default dynamic-dimension setter.</summary>
    /// <param name="value">默认范围。 The default range.</param>
    public void SetDefaultDynDimValue(MIGraphXDynamicDimension value) => SetDefaultDynamicDimensionValue(value);

    /// <summary>设置 Loop operator 缺少 trip count 时的默认最大迭代数。 Sets the default maximum Loop iterations when no trip count is present.</summary>
    /// <param name="value">非负最大迭代数。 A non-negative maximum iteration count.</param>
    public void SetDefaultLoopIterations(long value)
    {
        if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value), "Loop iterations must not be negative."); }
        owner.WithHandle(handle => NativeStatus.ThrowIfFailed(
            NativeMethods.OnnxOptionsSetDefaultLoopIterations(handle, value),
            "migraphx_onnx_options_set_default_loop_iterations"));
        lock (staticOverrides) { defaultLoopIterations = value; }
    }

    /// <summary>设置 Loop operator 的最大迭代安全上限。 Sets the maximum Loop-operator iteration safety limit.</summary>
    /// <param name="value">非负迭代上限。 A non-negative iteration limit.</param>
    public void SetLimitLoopIterations(long value)
    {
        if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value), "Loop iteration limits must not be negative."); }
        owner.WithHandle(handle => NativeStatus.ThrowIfFailed(
            NativeMethods.OnnxOptionsSetLimitLoopIterations(handle, value),
            "migraphx_onnx_options_set_limit_loop_iterations"));
        lock (staticOverrides) { limitLoopIterations = value; }
    }

    /// <summary>设置 ONNX external-data 文件的绝对根路径。 Sets the absolute root path for ONNX external-data files.</summary>
    /// <param name="path">绝对 external-data 路径。 The absolute external-data path.</param>
    public void SetExternalDataPath(string path)
    {
        if (path is null) { throw new ArgumentNullException(nameof(path)); }
        if (!Path.IsPathRooted(path)) { throw new ArgumentException("The ONNX external-data path must be absolute.", nameof(path)); }
        var fullPath = Path.GetFullPath(path);
        using (var utf8 = new StrictUtf8String(fullPath, nameof(path)))
        {
            owner.WithHandle(handle => NativeStatus.ThrowIfFailed(
                NativeMethods.OnnxOptionsSetExternalDataPath(handle, utf8.Pointer),
                "migraphx_onnx_options_set_external_data_path"));
        }
        lock (staticOverrides) { externalDataPath = fullPath; }
    }

    internal IReadOnlyDictionary<string, long[]> StaticOverrides
    {
        get { lock (staticOverrides) { return staticOverrides.ToDictionary(pair => pair.Key, pair => (long[])pair.Value.Clone(), StringComparer.Ordinal); } }
    }

    internal IReadOnlyDictionary<string, MIGraphXDynamicDimension[]> DynamicOverrides
    {
        get { lock (staticOverrides) { return dynamicOverrides.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal); } }
    }

    internal long? DefaultDimension { get { lock (staticOverrides) { return defaultDimension; } } }
    internal MIGraphXDynamicDimension? DefaultDynamicDimension { get { lock (staticOverrides) { return defaultDynamicDimension; } } }
    internal long? DefaultLoopIterations { get { lock (staticOverrides) { return defaultLoopIterations; } } }
    internal long? LimitLoopIterations { get { lock (staticOverrides) { return limitLoopIterations; } } }
    internal string? ExternalDataPath { get { lock (staticOverrides) { return externalDataPath; } } }

    /// <summary>确定性释放 owned options handle；重复调用安全。 Deterministically releases the owned options handle; repeated calls are safe.</summary>
    public void Dispose() => owner.Dispose();
}
