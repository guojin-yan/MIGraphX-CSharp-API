using System;
using System.Runtime.InteropServices;
#if MIGRAPHX_LIBRARYIMPORT_PATH
using System.Runtime.CompilerServices;
#endif

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static partial class NativeMethods
{
#if MIGRAPHX_LIBRARYIMPORT_PATH
    // This deliberately projects only the upstream no-attribute call shape:
    // operation, name, NULL attributes, and no variadic values.  It is not a
    // general C-varargs binding.
    [LibraryImport(LibraryName, EntryPoint = "migraphx_operation_create")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus OperationCreateNoAttributes(IntPtr operation, IntPtr name, IntPtr attributes);
#else
    // This deliberately projects only the upstream no-attribute call shape:
    // operation, name, NULL attributes, and no variadic values.  It is not a
    // general C-varargs binding.
    [DllImport(LibraryName, EntryPoint = "migraphx_operation_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus OperationCreateNoAttributes(IntPtr operation, IntPtr name, IntPtr attributes);
#endif
}
