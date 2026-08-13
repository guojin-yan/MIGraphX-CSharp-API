using System;
using System.IO;
using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Interop;
using JYPPX.ROCm.MIGraphXSharp.Loading;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 提供 M2 的单输入、单输出、静态 float32 ONNX 同步执行路径。
/// Provides the M2 single-input, single-output, static float32 ONNX synchronous execution path.
/// </summary>
/// <remarks>
/// 输入缓冲仅在同步调用期间固定；输出在原生集合释放前复制。动态 shape、多输入、多输出、非 float32、异步和设备缓冲不受支持。
/// The input buffer is pinned only for the synchronous call and output is copied before native collections are released. Dynamic shapes, multiple inputs or outputs, non-float32 data, async, and device buffers are unsupported.
/// </remarks>
public static class MIGraphXOnnxWorkflow
{
    /// <summary>
    /// 从绝对 ONNX 文件路径解析、编译并同步执行受限工作流。
    /// Parses, compiles, and synchronously executes the restricted workflow from an absolute ONNX file path.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="modelPath">ONNX 模型绝对文件路径。 Absolute path to the ONNX model file.</param>
    /// <param name="input">元素数量必须匹配模型输入的 float32 数据。 Float32 data whose element count must match the model input.</param>
    /// <param name="targetName">编译目标名称。 Compile-target name.</param>
    /// <returns>输入/输出 shape 与复制后的输出快照。 Input/output shapes and a copied output snapshot.</returns>
    public static MIGraphXOnnxExecutionResult RunFile(string nativeLibraryPath, string modelPath, float[] input, string targetName = "gpu")
    {
        if (modelPath is null) { throw new ArgumentNullException(nameof(modelPath)); }
        if (!Path.IsPathRooted(modelPath)) { throw new ArgumentException("The ONNX model path must be absolute.", nameof(modelPath)); }
        var fullPath = Path.GetFullPath(modelPath);
        using (var path = new StrictUtf8String(fullPath, nameof(modelPath)))
        {
            if (!File.Exists(fullPath)) { throw new FileNotFoundException("The ONNX model file does not exist.", fullPath); }
            return Execute(nativeLibraryPath, input, targetName, options => NativeProgramHandle.ParseFile(path.Pointer, options));
        }
    }

    /// <summary>
    /// 从内存 ONNX 字节解析、编译并同步执行受限工作流。
    /// Parses, compiles, and synchronously executes the restricted workflow from in-memory ONNX bytes.
    /// </summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="model">非空 ONNX protobuf 字节。 Non-empty ONNX protobuf bytes.</param>
    /// <param name="input">元素数量必须匹配模型输入的 float32 数据。 Float32 data whose element count must match the model input.</param>
    /// <param name="targetName">编译目标名称。 Compile-target name.</param>
    /// <returns>输入/输出 shape 与复制后的输出快照。 Input/output shapes and a copied output snapshot.</returns>
    public static MIGraphXOnnxExecutionResult RunBuffer(string nativeLibraryPath, byte[] model, float[] input, string targetName = "gpu")
    {
        if (model is null) { throw new ArgumentNullException(nameof(model)); }
        if (model.Length == 0) { throw new ArgumentException("The ONNX model buffer must not be empty.", nameof(model)); }
        var pinned = GCHandle.Alloc(model, GCHandleType.Pinned);
        try
        {
            return Execute(nativeLibraryPath, input, targetName, options => NativeProgramHandle.ParseBuffer(pinned.AddrOfPinnedObject(), new UIntPtr((uint)model.Length), options));
        }
        finally { pinned.Free(); }
    }

    private static MIGraphXOnnxExecutionResult Execute(string nativeLibraryPath, float[] input, string targetName, Func<NativeOnnxOptionsHandle, NativeProgramHandle> parse)
    {
        if (input is null) { throw new ArgumentNullException(nameof(input)); }
        if (input.Length == 0) { throw new ArgumentException("The input must not be empty.", nameof(input)); }
        var load = NativeLibraryLoader.LoadExplicit(nativeLibraryPath, requireOnnxWorkflow: true);
        if (!load.Success) { throw new MIGraphXNativeLoadException(load.Diagnostics); }

        using (var options = NativeOnnxOptionsHandle.Create())
        using (var program = parse(options))
        using (var parameterShapes = NativeProgramParameterShapesHandle.Create(program))
        {
            var parameterCount = NativeShapeSnapshot.ToInt(GetSize(parameterShapes), "parameter count");
            if (parameterCount != 1) { throw new NotSupportedException($"M2 supports exactly one model input; the model has {parameterCount}."); }
            var inputName = GetSingleParameterName(parameterShapes);
            using (var utf8Name = new StrictUtf8String(inputName, nameof(inputName)))
            {
                NativeStatus.ThrowIfFailed(NativeMethods.ProgramParameterShapesGet(out var inputShape, parameterShapes.DangerousGetHandle(), utf8Name.Pointer), "migraphx_program_parameter_shapes_get");
                var inputSnapshot = NativeShapeSnapshot.Create(inputShape, "input");
                inputSnapshot.RequireFloat32StaticStandard();
                if (inputSnapshot.Elements != input.Length) { throw new ArgumentException($"Input element count {input.Length} does not match model shape element count {inputSnapshot.Elements}.", nameof(input)); }

                using (var target = NativeTargetHandle.Create(targetName))
                using (var compileOptions = NativeCompileOptionsHandle.Create(offloadCopy: true))
                {
                    NativeStatus.ThrowIfFailed(NativeMethods.ProgramCompile(program.DangerousGetHandle(), target.DangerousGetHandle(), compileOptions.DangerousGetHandle()), "migraphx_program_compile");
                }

                using (var outputShapes = NativeShapesHandle.Create(program))
                {
                    var outputCount = NativeShapeSnapshot.ToInt(GetSize(outputShapes), "output shape count");
                    if (outputCount != 1) { throw new NotSupportedException($"M2 supports exactly one model output; the model has {outputCount}."); }
                    NativeStatus.ThrowIfFailed(NativeMethods.ShapesGet(out var outputShape, outputShapes.DangerousGetHandle(), UIntPtr.Zero), "migraphx_shapes_get");
                    var outputSnapshot = NativeShapeSnapshot.Create(outputShape, "output");
                    outputSnapshot.RequireFloat32StaticStandard();

                    var pinnedInput = GCHandle.Alloc(input, GCHandleType.Pinned);
                    try
                    {
                        using (var argument = NativeArgumentHandle.Create(inputShape, pinnedInput.AddrOfPinnedObject()))
                        using (var parameters = NativeProgramParametersHandle.Create())
                        {
                            NativeStatus.ThrowIfFailed(NativeMethods.ProgramParametersAdd(parameters.DangerousGetHandle(), utf8Name.Pointer, argument.DangerousGetHandle()), "migraphx_program_parameters_add");
                            using (var outputs = NativeArgumentsHandle.Run(program, parameters))
                            {
                                var runOutputCount = NativeShapeSnapshot.ToInt(GetSize(outputs), "run output count");
                                if (runOutputCount != 1) { throw new NotSupportedException($"M2 supports exactly one run output; MIGraphX returned {runOutputCount}."); }
                                NativeStatus.ThrowIfFailed(NativeMethods.ArgumentsGet(out var outputArgument, outputs.DangerousGetHandle(), UIntPtr.Zero), "migraphx_arguments_get");
                                NativeStatus.ThrowIfFailed(NativeMethods.ArgumentShape(out var runShape, outputArgument), "migraphx_argument_shape");
                                var runSnapshot = NativeShapeSnapshot.Create(runShape, "run output");
                                runSnapshot.RequireFloat32StaticStandard();
                                if (runSnapshot.Elements != outputSnapshot.Elements || !DimensionsEqual(runSnapshot.Dimensions, outputSnapshot.Dimensions))
                                {
                                    throw new InvalidOperationException("Run output shape differs from the compiled output shape.");
                                }
                                NativeStatus.ThrowIfFailed(NativeMethods.ArgumentBuffer(out var outputBuffer, outputArgument), "migraphx_argument_buffer");
                                if (outputBuffer == IntPtr.Zero) { throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, "migraphx_argument_buffer (success with null buffer)"); }
                                var copied = new float[runSnapshot.Elements];
                                Marshal.Copy(outputBuffer, copied, 0, copied.Length);
                                return new MIGraphXOnnxExecutionResult(inputName, inputSnapshot.Dimensions, runSnapshot.Dimensions, copied);
                            }
                        }
                    }
                    finally { pinnedInput.Free(); }
                }
            }
        }
    }

    private static UIntPtr GetSize(NativeProgramParameterShapesHandle shapes)
    {
        NativeStatus.ThrowIfFailed(NativeMethods.ProgramParameterShapesSize(out var size, shapes.DangerousGetHandle()), "migraphx_program_parameter_shapes_size");
        return size;
    }

    private static UIntPtr GetSize(NativeShapesHandle shapes)
    {
        NativeStatus.ThrowIfFailed(NativeMethods.ShapesSize(out var size, shapes.DangerousGetHandle()), "migraphx_shapes_size");
        return size;
    }

    private static UIntPtr GetSize(NativeArgumentsHandle arguments)
    {
        NativeStatus.ThrowIfFailed(NativeMethods.ArgumentsSize(out var size, arguments.DangerousGetHandle()), "migraphx_arguments_size");
        return size;
    }

    private static string GetSingleParameterName(NativeProgramParameterShapesHandle shapes)
    {
        var names = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.ProgramParameterShapesNames(names, shapes.DangerousGetHandle()), "migraphx_program_parameter_shapes_names");
            return StrictUtf8String.Decode(Marshal.ReadIntPtr(names), "migraphx_program_parameter_shapes_names");
        }
        finally { Marshal.FreeHGlobal(names); }
    }

    private static bool DimensionsEqual(long[] left, long[] right)
    {
        if (left.Length != right.Length) { return false; }
        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index]) { return false; }
        }
        return true;
    }
}
