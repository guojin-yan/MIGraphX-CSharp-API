using System;
using System.Linq;
using System.Runtime.Versioning;
using JYPPX.ROCm.MIGraphXSharp;

if (args.Length == 2 && args[0] == "--expect-m10-missing" && System.IO.Path.IsPathRooted(args[1]))
{
    try
    {
        _ = MIGraphXOnnxWorkflow.GetRegisteredOperators(args[1]);
        Console.Error.WriteLine("M10 registry unexpectedly accepted a library without the required exports.");
        return 1;
    }
    catch (MIGraphXNativeLoadException exception)
    {
        var diagnostic = exception.Diagnostics.Last();
        var missingPassed = diagnostic.Kind == JYPPX.ROCm.MIGraphXSharp.Diagnostics.MIGraphXNativeDiagnosticKind.ExportMissing
            && diagnostic.Message.IndexOf("M10 ONNX registry", StringComparison.Ordinal) >= 0
            && diagnostic.Message.IndexOf("migraphx_get_onnx_operators_size", StringComparison.Ordinal) >= 0
            && diagnostic.Message.IndexOf("migraphx_get_onnx_operator_name_at_index", StringComparison.Ordinal) >= 0;
        Console.WriteLine($"m10-missing-exports={(missingPassed ? "rejected" : "misclassified")}");
        return missingPassed ? 0 : 1;
    }
}

if (args.Length == 2 && args[0] == "--expect-m10-equality-missing" && System.IO.Path.IsPathRooted(args[1]))
{
    try
    {
        var equalityShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 1 });
        using var left = MIGraphXArgument.Create(args[1], equalityShape, new[] { 1f });
        using var right = MIGraphXArgument.Create(args[1], equalityShape, new[] { 1f });
        _ = left.HasSameNativeContent(right);
        Console.Error.WriteLine("M10 equality unexpectedly accepted a library without the required exports.");
        return 1;
    }
    catch (MIGraphXNativeLoadException exception)
    {
        var diagnostic = exception.Diagnostics.Last();
        var missingPassed = diagnostic.Kind == JYPPX.ROCm.MIGraphXSharp.Diagnostics.MIGraphXNativeDiagnosticKind.ExportMissing
            && diagnostic.Message.IndexOf("M10 content-equality", StringComparison.Ordinal) >= 0
            && diagnostic.Message.IndexOf("migraphx_argument_equal", StringComparison.Ordinal) >= 0
            && diagnostic.Message.IndexOf("migraphx_program_equal", StringComparison.Ordinal) >= 0;
        Console.WriteLine($"m10-equality-missing-exports={(missingPassed ? "rejected" : "misclassified")}");
        return missingPassed ? 0 : 1;
    }
}

if (args.Length != 2 || !System.IO.Path.IsPathRooted(args[0]) || !System.IO.Path.IsPathRooted(args[1]))
{
    Console.Error.WriteLine("Expected absolute fake-native and ONNX model paths.");
    return 2;
}

var report = MIGraphXEnvironment.Probe(args[0], exerciseObjects: true, targetName: "gpu-\u4e2d\u6587");
var framework = typeof(Program).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
    .Cast<TargetFrameworkAttribute>()
    .Single()
    .FrameworkName;
if (report.State != "executed" || !report.ExportsComplete || !report.ObjectsExecuted)
{
    foreach (var diagnostic in report.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.Kind}: {diagnostic.Message}");
    }
    return 1;
}

var input = new[] { 1f, -2f, 3.5f, 4f };
var onnx = MIGraphXOnnxWorkflow.RunBuffer(args[0], System.IO.File.ReadAllBytes(args[1]), input);
var operators = MIGraphXOnnxWorkflow.GetRegisteredOperators(args[0]);
var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
using var leftArgument = MIGraphXArgument.Create(args[0], shape, input);
using var rightArgument = MIGraphXArgument.Create(args[0], shape, input);
using var leftProgram = new MIGraphXProgram(args[0]);
using var rightProgram = new MIGraphXProgram(args[0]);
using var graphProgram = new MIGraphXProgram(args[0]);
using var graphModule = graphProgram.CreateModule("m12-cross-target");
var graphShape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
using var graphParameter = graphModule.AddParameter("input", graphShape);
using var graphAllocation = graphModule.AddAllocation(graphShape);
using var graphInstructions = new MIGraphXInstructions(args[0], new[] { graphParameter, graphAllocation });
using var graphReturn = graphModule.AddReturn(graphInstructions);
var m12Passed = graphInstructions.Count == 2;
var passed = report.Diagnostics.Any(item => item.Kind == JYPPX.ROCm.MIGraphXSharp.Diagnostics.MIGraphXNativeDiagnosticKind.Executed)
    && onnx.InputDimensions.SequenceEqual(new long[] { 1, 4 })
    && onnx.OutputDimensions.SequenceEqual(new long[] { 1, 4 })
    && onnx.Output.SequenceEqual(input)
    && operators.SequenceEqual(new[] { "Add", "\u52a0", "Relu" })
    && leftArgument.HasSameNativeContent(rightArgument)
    && leftProgram.HasSameNativeContent(rightProgram)
    && m12Passed;
Console.WriteLine($"framework={framework};m1={report.State};m2={(passed ? "executed" : "failed")};m10={(passed ? "executed" : "failed")};m12={(m12Passed ? "executed" : "failed")};exports={report.ExportsComplete};objects={report.ObjectsExecuted}");
return passed ? 0 : 1;
