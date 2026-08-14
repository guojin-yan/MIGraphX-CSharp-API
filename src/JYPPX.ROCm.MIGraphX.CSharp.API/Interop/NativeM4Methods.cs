using System;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeM4Methods
{
    internal static readonly string[] AdditionalRequiredExports =
    {
        "migraphx_shape_destroy",
        "migraphx_shape_create",
    };

    [DllImport(NativeMethods.LibraryName, EntryPoint = "migraphx_shape_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeCreate(
        out IntPtr shape,
        NativeMIGraphXShapeDataType type,
        IntPtr lengths,
        UIntPtr lengthsSize);
}
