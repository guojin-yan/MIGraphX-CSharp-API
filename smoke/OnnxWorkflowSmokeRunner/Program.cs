using System.Text.Json;
using System.Security.Cryptography;
using JYPPX.ROCm.MIGraphXSharp;
using JYPPX.ROCm.MIGraphXSharp.Diagnostics;

if (args.Length == 2 && args[0] == "--expect-frontend-missing")
{
    try
    {
        MIGraphXOnnxWorkflow.RunBuffer(args[1], new byte[] { 0x08, 0x08 }, new[] { 1f, 2f, 3f, 4f });
        Console.Error.WriteLine("The M1-only native library unexpectedly satisfied the M2 workflow.");
        return 1;
    }
    catch (MIGraphXNativeLoadException exception) when (exception.Diagnostics.Any(item => item.Kind == MIGraphXNativeDiagnosticKind.OnnxFrontendMissing))
    {
        Console.WriteLine(JsonSerializer.Serialize(new { state = "frontend-missing", diagnostic = "OnnxFrontendMissing" }));
        return 0;
    }
}

if (args.Length != 3 || args[0] != "--fake-native" && args[0] != "--runtime-candidate")
{
    Console.Error.WriteLine("Usage: --fake-native|--runtime-candidate <absolute-native-path> <absolute-model-path> | --expect-frontend-missing <absolute-native-path>");
    return 2;
}

var input = new[] { 0.25f, -1f, 2f, 9f };
var model = File.ReadAllBytes(args[2]);
var fromFile = MIGraphXOnnxWorkflow.RunFile(args[1], args[2], input);
var fromBuffer = MIGraphXOnnxWorkflow.RunBuffer(args[1], model, input);
var referenceMatched = fromFile.Output.SequenceEqual(input) && fromBuffer.Output.SequenceEqual(input);
if (!referenceMatched)
{
    Console.Error.WriteLine("Identity output did not match the input.");
    return 1;
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    state = "executed",
    modelSha256 = Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant(),
    inputName = fromFile.InputName,
    inputDimensions = fromFile.InputDimensions,
    outputDimensions = fromFile.OutputDimensions,
    output = fromFile.Output,
    paths = new[] { "file", "buffer" },
    referenceMatched,
    evidence = args[0] == "--fake-native" ? "fake-native-executed" : "runtime-candidate-executed",
}));
return 0;
