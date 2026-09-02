using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>TensorFlow 解析选项。 TensorFlow parser options.</summary>
public sealed class MIGraphXTfOptions : IDisposable
{
    private readonly NativeResourceOwner<NativeTfOptionsHandle> owner;
    private readonly Dictionary<string, long[]> staticOverrides = new Dictionary<string, long[]>(StringComparer.Ordinal);
    private readonly List<string> outputNames = new List<string>();

    /// <summary>使用显式 native library 创建 TensorFlow 选项。 Creates TensorFlow options from an explicit native library.</summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute MIGraphX C native-library path.</param>
    public MIGraphXTfOptions(string nativeLibraryPath)
        : this(NativeRuntime.Load(nativeLibraryPath), NativeTfOptionsHandle.Create())
    {
    }

    private MIGraphXTfOptions(NativeRuntime runtime, NativeTfOptionsHandle handle)
    {
        owner = new NativeResourceOwner<NativeTfOptionsHandle>(runtime, handle);
    }

    /// <summary>是否使用 NHWC 布局。 Whether NHWC layout is enabled.</summary>
    public bool IsNhwc { get; private set; }

    /// <summary>设置 NHWC 解析开关。 Sets the NHWC parser switch.</summary>
    /// <param name="enabled">是否启用 NHWC。 Whether NHWC is enabled.</param>
    public void SetNhwc(bool enabled)
    {
        owner.WithHandle(handle =>
        {
            NativeStatus.ThrowIfFailed(NativeMethods.TfOptionsSetNhwc(handle, enabled ? (byte)1 : (byte)0), "migraphx_tf_options_set_nhwc");
            IsNhwc = enabled;
        });
    }

    /// <summary>设置输入参数静态 shape。 Sets a static input-parameter shape.</summary>
    /// <param name="name">输入名称。 Input name.</param>
    /// <param name="dimensions">正维度长度。 Positive dimension lengths.</param>
    public void SetInputParameterShape(string name, IReadOnlyList<long> dimensions)
    {
        if (dimensions is null) { throw new ArgumentNullException(nameof(dimensions)); }
        var copied = dimensions.ToArray();
        for (var index = 0; index < copied.Length; index++)
        {
            if (copied[index] <= 0) { throw new ArgumentOutOfRangeException(nameof(dimensions), "TensorFlow input dimensions must be positive."); }
            MIGraphXDynamicDimension.ValidateSizeT(copied[index], nameof(dimensions));
        }
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        using (var values = NativeSizeTArray.Alloc(copied, nameof(dimensions)))
        {
            owner.WithHandle(handle =>
            {
                NativeStatus.ThrowIfFailed(NativeMethods.TfOptionsSetInputParameterShape(handle, utf8.Pointer, values.Pointer, NativeSizeTArray.Count(copied.Length)), "migraphx_tf_options_set_input_parameter_shape");
                lock (staticOverrides) { staticOverrides[name] = copied; }
            });
        }
    }

    /// <summary>设置默认动态维度的静态替代值。 Sets the default static replacement for dynamic dimensions.</summary>
    /// <param name="value">默认维度值。 Default dimension value.</param>
    public void SetDefaultDimValue(long value)
    {
        MIGraphXDynamicDimension.ValidateSizeT(value, nameof(value));
        owner.WithHandle(handle =>
        {
            NativeStatus.ThrowIfFailed(NativeMethods.TfOptionsSetDefaultDimValue(handle, MIGraphXDynamicDimension.ToNativeSizeT(value, nameof(value))), "migraphx_tf_options_set_default_dim_value");
            DefaultDimValue = value;
        });
    }

    /// <summary>默认动态维度替代值。 Gets the default dynamic-dimension replacement.</summary>
    public long? DefaultDimValue { get; private set; }

    /// <summary>设置输出名称集合。 Sets the output-name collection.</summary>
    /// <param name="names">输出名称。 Output names.</param>
    public void SetOutputNames(IReadOnlyList<string> names)
    {
        if (names is null) { throw new ArgumentNullException(nameof(names)); }
        var copied = names.ToArray();
        var utf8 = new StrictUtf8String?[copied.Length];
        var pointerBytes = checked(copied.Length * IntPtr.Size);
        var pointers = pointerBytes == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(pointerBytes);
        try
        {
            for (var index = 0; index < copied.Length; index++)
            {
                var value = new StrictUtf8String(copied[index], nameof(names));
                utf8[index] = value;
                Marshal.WriteIntPtr(pointers, index * IntPtr.Size, value.Pointer);
            }
            owner.WithHandle(handle =>
            {
                NativeStatus.ThrowIfFailed(NativeMethods.TfOptionsSetOutputNames(handle, pointers, NativeSizeTArray.Count(copied.Length)), "migraphx_tf_options_set_output_names");
                lock (staticOverrides)
                {
                    outputNames.Clear();
                    outputNames.AddRange(copied);
                }
            });
        }
        finally
        {
            foreach (var value in utf8) value?.Dispose();
            if (pointers != IntPtr.Zero) Marshal.FreeHGlobal(pointers);
        }
    }

    /// <summary>输出名称快照。 Gets the output-name snapshot.</summary>
    public IReadOnlyList<string> OutputNames
    {
        get
        {
            return owner.WithHandle(_ =>
            {
                lock (staticOverrides) { return (IReadOnlyList<string>)Array.AsReadOnly(outputNames.ToArray()); }
            });
        }
    }

    /// <summary>通过 native assign-to 创建独立副本。 Creates an independent copy through native assign-to.</summary>
    public MIGraphXTfOptions Clone()
    {
        return owner.WithHandle(handle =>
        {
            var result = new MIGraphXTfOptions(owner.Runtime, NativeTfOptionsHandle.CloneFrom(handle)) { IsNhwc = IsNhwc, DefaultDimValue = DefaultDimValue };
            lock (staticOverrides)
            {
                foreach (var pair in staticOverrides) result.staticOverrides[pair.Key] = (long[])pair.Value.Clone();
                result.outputNames.AddRange(outputNames);
            }
            return result;
        });
    }

    internal NativeResourceOwner<NativeTfOptionsHandle> Owner => owner;
    /// <summary>释放 native TensorFlow 选项。 Releases the native TensorFlow options.</summary>
    public void Dispose() => owner.Dispose();

    internal IReadOnlyDictionary<string, long[]> StaticOverrides
    {
        get { lock (staticOverrides) { return staticOverrides.ToDictionary(pair => pair.Key, pair => (long[])pair.Value.Clone(), StringComparer.Ordinal); } }
    }

    internal static string ValidateInputPath(string path, string parameterName)
    {
        if (path is null) { throw new ArgumentNullException(parameterName); }
        if (!Path.IsPathRooted(path)) { throw new ArgumentException("The path must be absolute.", parameterName); }
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) { throw new FileNotFoundException("The TensorFlow model file does not exist.", fullPath); }
        return fullPath;
    }
}
