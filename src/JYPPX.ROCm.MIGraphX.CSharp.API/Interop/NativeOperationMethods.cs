using System;
using System.Runtime.InteropServices;
#if MIGRAPHX_LIBRARYIMPORT_PATH
using System.Runtime.CompilerServices;
#endif

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static partial class NativeMethods
{
#if MIGRAPHX_LIBRARYIMPORT_PATH
    // This deliberately projects only the fixed operation/name/attributes call shape:
    // attributes are fully materialized text and no variadic values are supplied. It is
    // not a general C-varargs binding.
    [LibraryImport(LibraryName, EntryPoint = "migraphx_operation_create")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus OperationCreateNoAttributes(IntPtr operation, IntPtr name, IntPtr attributes);
#else
    // This deliberately projects only the fixed operation/name/attributes call shape:
    // attributes are fully materialized text and no variadic values are supplied. It is
    // not a general C-varargs binding.
    [DllImport(LibraryName, EntryPoint = "migraphx_operation_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus OperationCreateNoAttributes(IntPtr operation, IntPtr name, IntPtr attributes);
#endif

    // Same native entry point as OperationCreateNoAttributes; the distinct managed name
    // keeps the constrained text-attribute path explicit at call sites.
#if MIGRAPHX_LIBRARYIMPORT_PATH
    [LibraryImport(LibraryName, EntryPoint = "migraphx_operation_create")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial NativeMIGraphXStatus OperationCreateMaterializedAttributes(IntPtr operation, IntPtr name, IntPtr attributes);
#else
    [DllImport(LibraryName, EntryPoint = "migraphx_operation_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus OperationCreateMaterializedAttributes(IntPtr operation, IntPtr name, IntPtr attributes);
#endif
}
