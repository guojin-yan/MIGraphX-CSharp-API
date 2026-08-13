using System;
using System.Linq;
using System.Runtime.Versioning;
using JYPPX.ROCm.MIGraphXSharp;

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
var passed = report.Diagnostics.Any(item => item.Kind == JYPPX.ROCm.MIGraphXSharp.Diagnostics.MIGraphXNativeDiagnosticKind.Executed)
    && onnx.InputDimensions.SequenceEqual(new long[] { 1, 4 })
    && onnx.OutputDimensions.SequenceEqual(new long[] { 1, 4 })
    && onnx.Output.SequenceEqual(input);
Console.WriteLine($"framework={framework};m1={report.State};m2={(passed ? "executed" : "failed")};exports={report.ExportsComplete};objects={report.ObjectsExecuted}");
return passed ? 0 : 1;
