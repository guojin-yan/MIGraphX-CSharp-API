using System;
using System.Collections.Generic;
using JYPPX.ROCm.MIGraphXSharp.Interop;
using JYPPX.ROCm.MIGraphXSharp.Loading;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>可独立探测的原生功能。 Independently detectable native features.</summary>
public enum MIGraphXFeature
{
    /// <summary>TensorFlow 导入。 TensorFlow import.</summary>
    TensorFlow,
    /// <summary>符号动态维度。 Symbolic dynamic dimensions.</summary>
    SymbolicDimensions,
    /// <summary>执行跟踪。 Execution tracing.</summary>
    ExecutionTrace,
    /// <summary>带 JSON 选项的目标。 Targets with JSON options.</summary>
    TargetOptions,
    /// <summary>ONNX 符号和调试选项。 ONNX symbol and debug options.</summary>
    OnnxDimensionParameters,
    /// <summary>编译模式和后端选项。 Compile modes and backend options.</summary>
    AdvancedCompilation,
}

/// <summary>按导出探测当前原生库功能，不推测版本。 Detects native capabilities by exports without guessing versions.</summary>
public static class MIGraphXCapabilities
{
    /// <summary>获取功能缺失的导出；空集合表示接口可用，不保证设备执行成功。 Gets missing exports; an empty list indicates API availability, not successful device execution.</summary>
    /// <param name="nativeLibraryPath">原生库绝对路径。 Absolute native library path.</param>
    /// <param name="feature">待探测功能。 Feature to inspect.</param>
    public static IReadOnlyList<string> GetMissingExports(string nativeLibraryPath, MIGraphXFeature feature)
    {
        var exports = NativeFeatures.Exports(feature);
        _ = NativeRuntime.Load(nativeLibraryPath);
        return Array.AsReadOnly(NativeLibraryLoader.MissingActiveExports(exports));
    }

    /// <summary>检查原生功能所需导出是否齐全。 Checks whether all exports for a native feature exist.</summary>
    /// <param name="nativeLibraryPath">原生库绝对路径。 Absolute native library path.</param>
    /// <param name="feature">待探测功能。 Feature to inspect.</param>
    public static bool IsSupported(string nativeLibraryPath, MIGraphXFeature feature)
        => GetMissingExports(nativeLibraryPath, feature).Count == 0;
}

internal static class NativeFeatures
{
    internal static string[] Exports(MIGraphXFeature feature)
    {
        string[] names;
        switch (feature)
        {
            case MIGraphXFeature.TensorFlow:
                names = new[] { "tf_options_create", "tf_options_destroy", "tf_options_assign_to", "tf_options_set_nhwc", "tf_options_set_input_parameter_shape", "tf_options_set_default_dim_value", "tf_options_set_output_names", "parse_tf", "parse_tf_buffer" }; break;
            case MIGraphXFeature.SymbolicDimensions:
                names = new[] { "symbol_bounds_create", "symbol_bounds_destroy", "symbol_bounds_assign_to", "symbol_bounds_add", "dynamic_dimension_create_symbolic", "dynamic_dimension_is_symbolic" }; break;
            case MIGraphXFeature.ExecutionTrace:
                names = new[] { "trace_info_create", "trace_info_destroy", "trace_info_assign_to", "trace_info_get_index", "trace_info_get_name", "trace_info_get_result", "program_run_trace" }; break;
            case MIGraphXFeature.TargetOptions:
                names = new[] { "target_create_with_options" }; break;
            case MIGraphXFeature.OnnxDimensionParameters:
                names = new[] { "onnx_options_set_dim_param", "onnx_options_set_use_debug_symbols" }; break;
            case MIGraphXFeature.AdvancedCompilation:
                names = new[] { "compile_options_set_compile_mode", "compile_options_set_advance_backend_options" }; break;
            default: throw new ArgumentOutOfRangeException(nameof(feature));
        }
        for (var i = 0; i < names.Length; i++) names[i] = "migraphx_" + names[i];
        return names;
    }
}
