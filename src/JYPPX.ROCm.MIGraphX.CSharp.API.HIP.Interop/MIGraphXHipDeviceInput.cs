using System;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.MIGraphXSharp;

namespace JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;

/// <summary>描述由 HipSharp 设备分配支持的模型输入。 Describes a model input backed by a HipSharp device allocation.</summary>
public sealed class MIGraphXHipDeviceInput
{
    /// <summary>创建一个具名设备输入描述。 Creates a named device-input descriptor.</summary>
    /// <param name="name">模型参数名称。 Model parameter name.</param>
    /// <param name="shape">具体静态输入 shape。 Concrete static input shape.</param>
    /// <param name="memory">至少容纳 shape 字节数的设备分配。 Device allocation with capacity for the shape bytes.</param>
    public MIGraphXHipDeviceInput(string name, MIGraphXShape shape, HipDeviceMemory memory)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("The input name must not be empty.", nameof(name));
        Name = name;
        Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    /// <summary>获取模型参数名称。 Gets the model parameter name.</summary>
    public string Name { get; }

    /// <summary>获取具体静态 shape。 Gets the concrete static shape.</summary>
    public MIGraphXShape Shape { get; }

    /// <summary>获取调用方拥有的设备分配。 Gets the caller-owned device allocation.</summary>
    public HipDeviceMemory Memory { get; }
}
