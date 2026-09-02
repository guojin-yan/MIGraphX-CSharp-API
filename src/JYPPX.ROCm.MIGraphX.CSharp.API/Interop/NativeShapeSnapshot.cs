using System;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeShapeSnapshot
{
    private NativeShapeSnapshot(NativeMIGraphXShapeDataType type, long[] dimensions, long[] strides, long elements, long bytes, bool standard, bool dynamic, bool[] dynamicFixedFlags)
    {
        Type = type;
        Dimensions = dimensions;
        Strides = strides;
        Elements = elements;
        Bytes = bytes;
        Standard = standard;
        Dynamic = dynamic;
        DynamicFixedFlags = dynamicFixedFlags;
    }

    internal NativeMIGraphXShapeDataType Type { get; }
    internal long[] Dimensions { get; }
    internal long[] Strides { get; }
    internal long Elements { get; }
    internal long Bytes { get; }
    internal bool Standard { get; }
    internal bool Dynamic { get; }
    internal bool[] DynamicFixedFlags { get; }

    internal static NativeShapeSnapshot Create(IntPtr shape, string context)
    {
        if (shape == IntPtr.Zero) { throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, $"{context} shape (null borrowed handle)"); }
        NativeStatus.ThrowIfFailed(NativeMethods.ShapeType(out var type, shape), "migraphx_shape_type");
        NativeStatus.ThrowIfFailed(NativeMethods.ShapeDynamic(out var dynamic, shape), "migraphx_shape_dynamic");
        var isDynamic = NativeBoolean.Read(dynamic, "migraphx_shape_dynamic");
        if (isDynamic)
        {
            using var dimensions = NativeDynamicDimensionsHandle.FromShape(shape);
            return new NativeShapeSnapshot(
                type,
                Array.Empty<long>(),
                Array.Empty<long>(),
                0,
                0,
                false,
                true,
                dimensions.ReadFixedFlags());
        }

        NativeStatus.ThrowIfFailed(NativeMethods.ShapeLengths(out var lengths, out var lengthCount, shape), "migraphx_shape_lengths");
        NativeStatus.ThrowIfFailed(NativeMethods.ShapeStrides(out var strides, out var strideCount, shape), "migraphx_shape_strides");
        NativeStatus.ThrowIfFailed(NativeMethods.ShapeElements(out var elements, shape), "migraphx_shape_elements");
        NativeStatus.ThrowIfFailed(NativeMethods.ShapeBytes(out var bytes, shape), "migraphx_shape_bytes");
        NativeStatus.ThrowIfFailed(NativeMethods.ShapeStandard(out var standard, shape), "migraphx_shape_standard");
        return new NativeShapeSnapshot(type, CopySizeT(lengths, lengthCount, "shape lengths"), CopySizeT(strides, strideCount, "shape strides"), ToLong(elements, "shape elements"), ToLong(bytes, "shape bytes"), NativeBoolean.Read(standard, "migraphx_shape_standard"), false, Array.Empty<bool>());
    }

    internal void RequireFloat32StaticStandard()
    {
        if (Type != NativeMIGraphXShapeDataType.Float) { throw new NotSupportedException($"M2 supports only float32 tensors; native type was {Type}."); }
        if (Dynamic) { throw new NotSupportedException("M2 does not support dynamic tensor shapes."); }
        if (!Standard) { throw new NotSupportedException("M2 requires standard contiguous tensor shapes."); }
        if (Bytes != checked(Elements * sizeof(float))) { throw new InvalidOperationException("Native shape byte count does not match float32 element count."); }
    }

    internal static int ToInt(UIntPtr value, string name)
    {
        var number = value.ToUInt64();
        if (number > int.MaxValue) { throw new OverflowException($"Native {name} exceeds the managed array limit."); }
        return (int)number;
    }

    internal static int ToInt(long value, string name)
    {
        if (value < 0 || value > int.MaxValue) { throw new OverflowException($"Native {name} exceeds the managed array limit."); }
        return (int)value;
    }

    internal static long ToLong(UIntPtr value, string name)
    {
        var number = value.ToUInt64();
        if (number > long.MaxValue) { throw new OverflowException($"Native {name} exceeds Int64."); }
        return (long)number;
    }

    private static long[] CopySizeT(IntPtr pointer, UIntPtr countValue, string name)
    {
        var count = ToInt(countValue, name);
        if (count != 0 && pointer == IntPtr.Zero) { throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, $"{name} (success with null buffer)"); }
        var values = new long[count];
        for (var index = 0; index < count; index++)
        {
            ulong value = UIntPtr.Size == 8
                ? unchecked((ulong)Marshal.ReadInt64(pointer, index * UIntPtr.Size))
                : unchecked((uint)Marshal.ReadInt32(pointer, index * UIntPtr.Size));
            if (value > long.MaxValue) { throw new OverflowException($"Native {name} value exceeds Int64."); }
            values[index] = (long)value;
        }
        return values;
    }
}
