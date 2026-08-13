using System.Text.Json;
using JYPPX.ROCm.MIGraphXSharp;

var result = new
{
    schemaVersion = "1.0.0",
    managedPackageVersion = MIGraphXBuildInfo.PackageVersion,
    nativeProbe = "not-probed",
    nativeLibrary = "not-available",
    onnxFrontend = "not-probed",
    amdGpu = "not-probed",
    message = "M0 does not load MIGraphX or claim native, ONNX, or GPU availability.",
};

Console.WriteLine(JsonSerializer.Serialize(result));
return 0;
