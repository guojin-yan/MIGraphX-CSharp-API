using System.Text.Json;
using JYPPX.ROCm.MIGraphXSharp;

if (args.Length == 0)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0.0",
        managedPackageVersion = MIGraphXBuildInfo.PackageVersion,
        evidenceLevel = "managed-only",
        nativeProbe = "not-probed",
        nativeLibrary = "not-available",
        exports = "not-probed",
        target = "not-probed",
        program = "not-probed",
        onnxFrontend = "not-applicable-m1",
        amdGpu = "not-probed",
        message = "No explicit native path was supplied; the smoke runner performed no native loading.",
    }));
    return 0;
}

var isFakeNative = args.Length == 2 && args[0] == "--fake-native";
var isUtf8Probe = args.Length == 2 && args[0] == "--utf8-probe";
var nativePath = isFakeNative || isUtf8Probe ? args[1] : args.Length == 1 ? args[0] : string.Empty;
if (string.IsNullOrEmpty(nativePath) || !Path.IsPathRooted(nativePath))
{
    Console.Error.WriteLine("Usage: EnvironmentSmokeRunner [--fake-native|--utf8-probe] <absolute-native-library-path>");
    return 2;
}

try
{
    var utf8Target = "not-requested";
    int? utf8StatusCode = null;
    string? utf8Operation = null;
    if (isUtf8Probe)
    {
        try
        {
            var utf8Report = MIGraphXEnvironment.Probe(nativePath, exerciseObjects: true, targetName: "gpu-\u4e2d\u6587-\u03a9");
            if (!utf8Report.ObjectsExecuted)
            {
                throw new InvalidOperationException("The non-ASCII target probe neither executed nor returned a native status.");
            }

            utf8Target = "runtime-accepted";
        }
        catch (MIGraphXException exception) when (
            exception.Operation == "migraphx_target_create"
            && (exception.KnownStatus == MIGraphXStatus.UnknownTarget || exception.KnownStatus == MIGraphXStatus.UnknownError))
        {
            utf8Target = "marshalled-runtime-rejected-unknown-target";
            utf8StatusCode = exception.StatusCode;
            utf8Operation = exception.Operation;
        }
    }

    var report = MIGraphXEnvironment.Probe(nativePath, exerciseObjects: true, targetName: "gpu");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0.0",
        managedPackageVersion = MIGraphXBuildInfo.PackageVersion,
        evidenceLevel = report.ObjectsExecuted ? isFakeNative ? "fake-native-executed" : "runtime-executed" : "not-available",
        nativeProbe = "explicit-path",
        nativeLibrary = report.State,
        exports = report.ExportsComplete ? "verified" : "not-available",
        target = report.ObjectsExecuted ? "executed" : "not-executed",
        program = report.ObjectsExecuted ? "executed" : "not-executed",
        utf8Target,
        utf8StatusCode,
        utf8Operation,
        onnxFrontend = "not-applicable-m1",
        amdGpu = "not-probed",
        diagnostics = report.Diagnostics.Select(item => new
        {
            kind = item.Kind.ToString(),
            item.Candidate,
            item.Source,
            item.FileExists,
            item.Message,
        }),
    }));
    return report.ObjectsExecuted ? 0 : 1;
}
catch (Exception exception)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0.0",
        evidenceLevel = isFakeNative ? "fake-native-failed" : "runtime-failed",
        nativeProbe = "explicit-path",
        nativeLibrary = "failed",
        utf8Target = isUtf8Probe ? "failed" : "not-requested",
        exception = exception.GetType().FullName,
        exception.Message,
    }));
    return 1;
}
