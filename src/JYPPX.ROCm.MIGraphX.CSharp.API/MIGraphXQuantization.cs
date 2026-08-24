using System;
using System.Collections.Generic;
using System.Linq;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>量化 operator 名称集合。 Native quantization operator-name set.</summary>
public sealed class MIGraphXQuantizeOpNames : IDisposable
{
    private readonly NativeResourceOwner<NativeQuantizeOpNamesHandle> owner;
    private readonly List<string> names;

    /// <summary>使用显式 native library 创建名称集合。 Creates the name set from an explicit native library.</summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute MIGraphX C native-library path.</param>
    public MIGraphXQuantizeOpNames(string nativeLibraryPath)
        : this(NativeRuntime.Load(nativeLibraryPath), NativeQuantizeOpNamesHandle.Create(), new List<string>())
    {
    }

    private MIGraphXQuantizeOpNames(NativeRuntime runtime, NativeQuantizeOpNamesHandle handle, List<string> names)
    {
        owner = new NativeResourceOwner<NativeQuantizeOpNamesHandle>(runtime, handle);
        this.names = names;
    }

    /// <summary>添加一个严格 UTF-8 operator 名称。 Adds a strict UTF-8 operator name.</summary>
    /// <param name="name">operator 名称。 Operator name.</param>
    public void Add(string name)
    {
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        {
            lock (owner.Sync)
            {
                _ = owner.HandleUnderLock;
                NativeStatus.ThrowIfFailed(NativeMethods.QuantizeOpNamesAdd(owner.HandleUnderLock, utf8.Pointer), "migraphx_quantize_op_names_add");
                if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
            }
        }
    }

    /// <summary>获取名称快照。 Gets an immutable name snapshot.</summary>
    public IReadOnlyList<string> Names => owner.WithHandle(_ => Array.AsReadOnly(names.ToArray()));

    /// <summary>通过 native assign-to 创建独立副本。 Creates an independent copy through native assign-to.</summary>
    public MIGraphXQuantizeOpNames Clone()
        => owner.WithHandle(handle => new MIGraphXQuantizeOpNames(owner.Runtime, NativeQuantizeOpNamesHandle.CloneFrom(handle), new List<string>(names)));

    internal NativeResourceOwner<NativeQuantizeOpNamesHandle> Owner => owner;
    /// <summary>释放 native 名称集合。 Releases the native name set.</summary>
    public void Dispose() => owner.Dispose();
}

/// <summary>INT8 量化选项。 INT8 quantization options.</summary>
public sealed class MIGraphXQuantizeInt8Options : IDisposable
{
    private readonly NativeResourceOwner<NativeQuantizeInt8OptionsHandle> owner;
    private readonly List<string> opNames = new List<string>();

    /// <summary>使用显式 native library 创建 INT8 选项。 Creates INT8 options from an explicit native library.</summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute MIGraphX C native-library path.</param>
    public MIGraphXQuantizeInt8Options(string nativeLibraryPath)
        : this(NativeRuntime.Load(nativeLibraryPath), NativeQuantizeInt8OptionsHandle.Create())
    {
    }

    private MIGraphXQuantizeInt8Options(NativeRuntime runtime, NativeQuantizeInt8OptionsHandle handle)
    {
        owner = new NativeResourceOwner<NativeQuantizeInt8OptionsHandle>(runtime, handle);
    }

    /// <summary>添加要量化的 operator 名称。 Adds an operator name to quantize.</summary>
    /// <param name="name">operator 名称。 Operator name.</param>
    public void AddOpName(string name)
    {
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        {
            owner.WithHandle(handle =>
            {
                NativeStatus.ThrowIfFailed(NativeMethods.QuantizeInt8OptionsAddOpName(handle, utf8.Pointer), "migraphx_quantize_int8_options_add_op_name");
                lock (opNames) { if (!opNames.Contains(name, StringComparer.Ordinal)) opNames.Add(name); }
            });
        }
    }

    /// <summary>添加 calibration parameter map。 Adds a calibration parameter map.</summary>
    /// <param name="data">同一 native library 的校准参数。 Calibration parameters from the same native library.</param>
    public void AddCalibrationData(MIGraphXParameterMap data)
    {
        if (data is null) { throw new ArgumentNullException(nameof(data)); }
        owner.Runtime.RequireSame(data.Owner.Runtime, nameof(data));
        NativeResourceLock.With(
            new[] { NativeResourceLock.Target(owner.Id, owner.Sync), NativeResourceLock.Target(data.Owner.Id, data.Owner.Sync) },
            () => NativeStatus.ThrowIfFailed(NativeMethods.QuantizeInt8OptionsAddCalibrationData(owner.HandleUnderLock, data.Owner.HandleUnderLock), "migraphx_quantize_int8_options_add_calibration_data"));
    }

    /// <summary>operator 名称快照。 Gets the operator-name snapshot.</summary>
    public IReadOnlyList<string> OpNames { get { lock (opNames) { return Array.AsReadOnly(opNames.ToArray()); } } }

    /// <summary>通过 native assign-to 创建独立副本。 Creates an independent copy through native assign-to.</summary>
    public MIGraphXQuantizeInt8Options Clone()
        => owner.WithHandle(handle => { var result = new MIGraphXQuantizeInt8Options(owner.Runtime, NativeQuantizeInt8OptionsHandle.CloneFrom(handle)); lock (opNames) { result.opNames.AddRange(opNames); } return result; });

    internal NativeResourceOwner<NativeQuantizeInt8OptionsHandle> Owner => owner;
    /// <summary>释放 native INT8 选项。 Releases the native INT8 options.</summary>
    public void Dispose() => owner.Dispose();
}

/// <summary>FP8 量化选项。 FP8 quantization options.</summary>
public sealed class MIGraphXQuantizeFp8Options : IDisposable
{
    private readonly NativeResourceOwner<NativeQuantizeFp8OptionsHandle> owner;

    /// <summary>使用显式 native library 创建 FP8 选项。 Creates FP8 options from an explicit native library.</summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute MIGraphX C native-library path.</param>
    public MIGraphXQuantizeFp8Options(string nativeLibraryPath)
        : this(NativeRuntime.Load(nativeLibraryPath), NativeQuantizeFp8OptionsHandle.Create())
    {
    }

    private MIGraphXQuantizeFp8Options(NativeRuntime runtime, NativeQuantizeFp8OptionsHandle handle)
    {
        owner = new NativeResourceOwner<NativeQuantizeFp8OptionsHandle>(runtime, handle);
    }

    /// <summary>添加 calibration parameter map。 Adds a calibration parameter map.</summary>
    /// <param name="data">同一 native library 的校准参数。 Calibration parameters from the same native library.</param>
    public void AddCalibrationData(MIGraphXParameterMap data)
    {
        if (data is null) { throw new ArgumentNullException(nameof(data)); }
        owner.Runtime.RequireSame(data.Owner.Runtime, nameof(data));
        NativeResourceLock.With(
            new[] { NativeResourceLock.Target(owner.Id, owner.Sync), NativeResourceLock.Target(data.Owner.Id, data.Owner.Sync) },
            () => NativeStatus.ThrowIfFailed(NativeMethods.QuantizeFp8OptionsAddCalibrationData(owner.HandleUnderLock, data.Owner.HandleUnderLock), "migraphx_quantize_fp8_options_add_calibration_data"));
    }

    /// <summary>通过 native assign-to 创建独立副本。 Creates an independent copy through native assign-to.</summary>
    public MIGraphXQuantizeFp8Options Clone()
        => owner.WithHandle(handle => new MIGraphXQuantizeFp8Options(owner.Runtime, NativeQuantizeFp8OptionsHandle.CloneFrom(handle)));

    internal NativeResourceOwner<NativeQuantizeFp8OptionsHandle> Owner => owner;
    /// <summary>释放 native FP8 选项。 Releases the native FP8 options.</summary>
    public void Dispose() => owner.Dispose();
}
