using System;
using System.Linq;
using System.Runtime.Versioning;
using JYPPX.ROCm.MIGraphXSharp;

if (args.Length != 1 || !System.IO.Path.IsPathRooted(args[0]))
{
    Console.Error.WriteLine("Expected one absolute fake-native path.");
    return 2;
}

var report = MIGraphXEnvironment.Probe(args[0], exerciseObjects: true, targetName: "gpu-\u4e2d\u6587");
var framework = typeof(Program).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
    .Cast<TargetFrameworkAttribute>()
    .Single()
    .FrameworkName;
Console.WriteLine($"framework={framework};state={report.State};exports={report.ExportsComplete};objects={report.ObjectsExecuted}");
if (report.State != "executed" || !report.ExportsComplete || !report.ObjectsExecuted)
{
    foreach (var diagnostic in report.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.Kind}: {diagnostic.Message}");
    }
    return 1;
}

return report.Diagnostics.Any(item => item.Kind == JYPPX.ROCm.MIGraphXSharp.Diagnostics.MIGraphXNativeDiagnosticKind.Executed) ? 0 : 1;
