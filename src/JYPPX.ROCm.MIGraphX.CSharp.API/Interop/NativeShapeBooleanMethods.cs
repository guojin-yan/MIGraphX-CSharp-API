using System;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static partial class NativeMethods
{
#if MIGRAPHX_LIBRARYIMPORT_PATH
    [LibraryImport(LibraryName, EntryPoint = "migraphx_shape_dynamic")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ShapeDynamicRaw(IntPtr output, IntPtr shape);

    [LibraryImport(LibraryName, EntryPoint = "migraphx_shape_standard")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus ShapeStandardRaw(IntPtr output, IntPtr shape);
#else
    [DllImport(LibraryName, EntryPoint = "migraphx_shape_dynamic", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeDynamicRaw(IntPtr output, IntPtr shape);

    [DllImport(LibraryName, EntryPoint = "migraphx_shape_standard", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeStandardRaw(IntPtr output, IntPtr shape);
#endif
}
