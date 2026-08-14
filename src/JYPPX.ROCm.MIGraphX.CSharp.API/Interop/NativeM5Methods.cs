namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeM5Methods
{
    internal static readonly string[] AdditionalRequiredExports =
    {
        "migraphx_optimals_create",
        "migraphx_optimals_destroy",
        "migraphx_dynamic_dimension_create_min_max",
        "migraphx_dynamic_dimension_create_min_max_optimals",
        "migraphx_dynamic_dimension_is_fixed",
        "migraphx_dynamic_dimension_equal",
        "migraphx_dynamic_dimension_destroy",
        "migraphx_dynamic_dimensions_create",
        "migraphx_dynamic_dimensions_destroy",
        "migraphx_dynamic_dimensions_size",
        "migraphx_dynamic_dimensions_get",
        "migraphx_shape_create_dynamic",
        "migraphx_shape_dyn_dims",
        "migraphx_onnx_options_set_input_parameter_shape",
        "migraphx_onnx_options_set_dyn_input_parameter_shape",
        "migraphx_onnx_options_set_default_dim_value",
        "migraphx_onnx_options_set_default_dyn_dim_value",
        "migraphx_file_options_create",
        "migraphx_file_options_set_file_format",
        "migraphx_file_options_destroy",
        "migraphx_save",
        "migraphx_load",
    };
}
