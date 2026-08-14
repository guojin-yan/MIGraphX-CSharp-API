namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示 M4 高层静态 shape 支持的标量元素类型。
/// Represents a scalar element type supported by the M4 high-level static-shape API.
/// </summary>
public enum MIGraphXShapeDataType
{
    /// <summary>32 位 IEEE 754 浮点数。 32-bit IEEE 754 floating point.</summary>
    Float32 = 4,

    /// <summary>64 位 IEEE 754 浮点数。 64-bit IEEE 754 floating point.</summary>
    Float64 = 5,

    /// <summary>8 位无符号整数。 8-bit unsigned integer.</summary>
    UInt8 = 6,

    /// <summary>8 位有符号整数。 8-bit signed integer.</summary>
    Int8 = 7,

    /// <summary>16 位无符号整数。 16-bit unsigned integer.</summary>
    UInt16 = 8,

    /// <summary>16 位有符号整数。 16-bit signed integer.</summary>
    Int16 = 9,

    /// <summary>32 位有符号整数。 32-bit signed integer.</summary>
    Int32 = 10,

    /// <summary>64 位有符号整数。 64-bit signed integer.</summary>
    Int64 = 11,

    /// <summary>32 位无符号整数。 32-bit unsigned integer.</summary>
    UInt32 = 12,

    /// <summary>64 位无符号整数。 64-bit unsigned integer.</summary>
    UInt64 = 13,
}
