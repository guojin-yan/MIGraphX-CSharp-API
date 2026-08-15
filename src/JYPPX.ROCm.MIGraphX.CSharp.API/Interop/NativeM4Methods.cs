using System;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeM4Methods
{
    internal static readonly string[] AdditionalRequiredExports =
    {
        "migraphx_shape_destroy",
        "migraphx_shape_create",
        "migraphx_onnx_options_set_default_loop_iterations",
        "migraphx_onnx_options_set_limit_loop_iterations",
        "migraphx_onnx_options_set_external_data_path",
        "migraphx_compile_options_set_fast_math",
        "migraphx_compile_options_set_exhaustive_tune_flag",
    };

    [DllImport(NativeMethods.LibraryName, EntryPoint = "migraphx_shape_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern NativeMIGraphXStatus ShapeCreate(
        out IntPtr shape,
        NativeMIGraphXShapeDataType type,
        IntPtr lengths,
        UIntPtr lengthsSize);
}
