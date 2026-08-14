using System;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class ShapeDataTypeMap
{
    internal static NativeMIGraphXShapeDataType ToNative(MIGraphXShapeDataType type)
    {
        if (!Enum.IsDefined(typeof(MIGraphXShapeDataType), type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "The shape data type is outside the supported M4 range.");
        }

        return (NativeMIGraphXShapeDataType)(int)type;
    }

    internal static MIGraphXShapeDataType FromNative(NativeMIGraphXShapeDataType type)
    {
        var value = (MIGraphXShapeDataType)(int)type;
        if (!Enum.IsDefined(typeof(MIGraphXShapeDataType), value))
        {
            throw new NotSupportedException($"Native shape data type '{type}' is outside the mapped M4 scalar set.");
        }

        return value;
    }

    internal static int ElementSize(MIGraphXShapeDataType type)
    {
        switch (type)
        {
            case MIGraphXShapeDataType.UInt8:
            case MIGraphXShapeDataType.Int8:
                return 1;
            case MIGraphXShapeDataType.UInt16:
            case MIGraphXShapeDataType.Int16:
                return 2;
            case MIGraphXShapeDataType.Float32:
            case MIGraphXShapeDataType.Int32:
            case MIGraphXShapeDataType.UInt32:
                return 4;
            case MIGraphXShapeDataType.Float64:
            case MIGraphXShapeDataType.Int64:
            case MIGraphXShapeDataType.UInt64:
                return 8;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "The shape data type is outside the supported M4 range.");
        }
    }

    internal static MIGraphXShapeDataType FromManagedType(Type type)
    {
        if (type == typeof(float)) { return MIGraphXShapeDataType.Float32; }
        if (type == typeof(double)) { return MIGraphXShapeDataType.Float64; }
        if (type == typeof(byte)) { return MIGraphXShapeDataType.UInt8; }
        if (type == typeof(sbyte)) { return MIGraphXShapeDataType.Int8; }
        if (type == typeof(ushort)) { return MIGraphXShapeDataType.UInt16; }
        if (type == typeof(short)) { return MIGraphXShapeDataType.Int16; }
        if (type == typeof(int)) { return MIGraphXShapeDataType.Int32; }
        if (type == typeof(long)) { return MIGraphXShapeDataType.Int64; }
        if (type == typeof(uint)) { return MIGraphXShapeDataType.UInt32; }
        if (type == typeof(ulong)) { return MIGraphXShapeDataType.UInt64; }
        throw new NotSupportedException($"Managed element type '{type.FullName}' has no M4 MIGraphX scalar mapping.");
    }
}
