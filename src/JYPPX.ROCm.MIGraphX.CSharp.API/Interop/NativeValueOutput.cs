using System;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static partial class NativeMethods
{
#if MIGRAPHX_LIBRARYIMPORT_PATH
    [LibraryImport(LibraryName, EntryPoint = "migraphx_program_parameter_shapes_size")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ProgramParameterShapesSizeRaw(IntPtr output, IntPtr shapes);

    [LibraryImport(LibraryName, EntryPoint = "migraphx_arguments_size")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ArgumentsSizeRaw(IntPtr output, IntPtr arguments);

    [LibraryImport(LibraryName, EntryPoint = "migraphx_shapes_size")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ShapesSizeRaw(IntPtr output, IntPtr shapes);

    [LibraryImport(LibraryName, EntryPoint = "migraphx_shape_lengths")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ShapeLengthsRaw(IntPtr output, IntPtr outputSize, IntPtr shape);

    [LibraryImport(LibraryName, EntryPoint = "migraphx_shape_strides")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ShapeStridesRaw(IntPtr output, IntPtr outputSize, IntPtr shape);

    [LibraryImport(LibraryName, EntryPoint = "migraphx_shape_type")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ShapeTypeRaw(IntPtr output, IntPtr shape);

    [LibraryImport(LibraryName, EntryPoint = "migraphx_shape_elements")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ShapeElementsRaw(IntPtr output, IntPtr shape);

    [LibraryImport(LibraryName, EntryPoint = "migraphx_shape_bytes")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ShapeBytesRaw(IntPtr output, IntPtr shape);
#else
    [DllImport(LibraryName, EntryPoint = "migraphx_program_parameter_shapes_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ProgramParameterShapesSizeRaw(IntPtr output, IntPtr shapes);

    [DllImport(LibraryName, EntryPoint = "migraphx_arguments_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ArgumentsSizeRaw(IntPtr output, IntPtr arguments);

    [DllImport(LibraryName, EntryPoint = "migraphx_shapes_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapesSizeRaw(IntPtr output, IntPtr shapes);

    [DllImport(LibraryName, EntryPoint = "migraphx_shape_lengths", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeLengthsRaw(IntPtr output, IntPtr outputSize, IntPtr shape);

    [DllImport(LibraryName, EntryPoint = "migraphx_shape_strides", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeStridesRaw(IntPtr output, IntPtr outputSize, IntPtr shape);

    [DllImport(LibraryName, EntryPoint = "migraphx_shape_type", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeTypeRaw(IntPtr output, IntPtr shape);

    [DllImport(LibraryName, EntryPoint = "migraphx_shape_elements", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeElementsRaw(IntPtr output, IntPtr shape);

    [DllImport(LibraryName, EntryPoint = "migraphx_shape_bytes", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeBytesRaw(IntPtr output, IntPtr shape);
#endif
}

internal static class NativeValueOutput
{
    private const byte Sentinel = 0xA5;

    internal static UIntPtr ReadSizeT(Func<IntPtr, NativeMIGraphXStatus> invoke, string operation)
    {
        var slot = Marshal.AllocHGlobal(UIntPtr.Size);
        try
        {
            FillSentinel(slot, UIntPtr.Size);
            NativeStatus.ThrowIfFailed(invoke(slot), operation);
            EnsureWritten(slot, UIntPtr.Size, operation, "size_t");
            return UIntPtr.Size == 8
                ? new UIntPtr(unchecked((ulong)Marshal.ReadInt64(slot)))
                : new UIntPtr(unchecked((uint)Marshal.ReadInt32(slot)));
        }
        finally { Marshal.FreeHGlobal(slot); }
    }

    internal static int ReadInt32(Func<IntPtr, NativeMIGraphXStatus> invoke, string operation)
    {
        const int size = sizeof(int);
        var slot = Marshal.AllocHGlobal(size);
        try
        {
            FillSentinel(slot, size);
            NativeStatus.ThrowIfFailed(invoke(slot), operation);
            EnsureWritten(slot, size, operation, "int32");
            return Marshal.ReadInt32(slot);
        }
        finally { Marshal.FreeHGlobal(slot); }
    }

    internal static void ReadPointerAndSize(
        Func<IntPtr, IntPtr, NativeMIGraphXStatus> invoke,
        string operation,
        out IntPtr pointer,
        out UIntPtr size)
    {
        var pointerSlot = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var sizeSlot = Marshal.AllocHGlobal(UIntPtr.Size);
            try
            {
                FillSentinel(pointerSlot, IntPtr.Size);
                FillSentinel(sizeSlot, UIntPtr.Size);
                NativeStatus.ThrowIfFailed(invoke(pointerSlot, sizeSlot), operation);
                EnsureWritten(pointerSlot, IntPtr.Size, operation, "pointer");
                EnsureWritten(sizeSlot, UIntPtr.Size, operation, "size_t");
                pointer = Marshal.ReadIntPtr(pointerSlot);
                size = UIntPtr.Size == 8
                    ? new UIntPtr(unchecked((ulong)Marshal.ReadInt64(sizeSlot)))
                    : new UIntPtr(unchecked((uint)Marshal.ReadInt32(sizeSlot)));
            }
            finally { Marshal.FreeHGlobal(sizeSlot); }
        }
        finally { Marshal.FreeHGlobal(pointerSlot); }
    }

    private static void FillSentinel(IntPtr slot, int size)
    {
        for (var index = 0; index < size; index++) { Marshal.WriteByte(slot, index, Sentinel); }
    }

    private static void EnsureWritten(IntPtr slot, int size, string operation, string outputType)
    {
        for (var index = 0; index < size; index++)
        {
            if (Marshal.ReadByte(slot, index) != Sentinel) { return; }
        }
        throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, $"{operation} (success without writing {outputType} output)");
    }
}
