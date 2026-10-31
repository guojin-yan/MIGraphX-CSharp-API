using System.Text.Json;
using System.Security.Cryptography;
using JYPPX.ROCm.MIGraphXSharp;
using JYPPX.ROCm.MIGraphXSharp.Diagnostics;

if (args.Length == 2 && args[0] == "--expect-frontend-missing")
{
    try
    {
        MIGraphXOnnxWorkflow.RunBuffer(args[1], new byte[] { 0x08, 0x08 }, new[] { 1f, 2f, 3f, 4f });
        Console.Error.WriteLine("The environment-only native library unexpectedly satisfied the ONNX workflow.");
        return 1;
    }
    catch (MIGraphXNativeLoadException exception) when (exception.Diagnostics.Any(item => item.Kind == MIGraphXNativeDiagnosticKind.OnnxFrontendMissing))
    {
        Console.WriteLine(JsonSerializer.Serialize(new { state = "frontend-missing", diagnostic = "OnnxFrontendMissing" }));
        return 0;
    }
}

if (args.Length == 3 && args[0] == "--runtime-options-candidate")
{
    try
    {
        var nativePath = args[1];
        var modelPath = Path.GetFullPath(args[2]);
        var optionInput = new[] { 0.25f, -1f, 2f, 9f };
        var optionModel = File.ReadAllBytes(modelPath);
        using var options = new MIGraphXOnnxOptions(nativePath);
        options.SetDefaultLoopIterations(10);
        options.SetLimitLoopIterations(ushort.MaxValue);
        options.SetExternalDataPath(Path.GetDirectoryName(modelPath)!);
        using var program = MIGraphXProgram.ParseOnnxFile(modelPath, options);
        using var target = new MIGraphXTarget(nativePath);
        using var compileOptions = new MIGraphXCompileOptions(nativePath, offloadCopy: true, fastMath: true, exhaustiveTune: false);
        program.Compile(target, compileOptions);
        var parameter = program.GetParameterShapes().Single();
        using var argument = MIGraphXArgument.Create(nativePath, parameter.Value, optionInput);
        using var parameters = new MIGraphXParameterMap(nativePath);
        parameters.Add(parameter.Key, argument);
        using var outputs = program.Run(parameters);
        var output = outputs.Single().ToArray<float>();
        var optionReferenceMatched = output.SequenceEqual(optionInput);
        if (!optionReferenceMatched) { throw new InvalidOperationException("ONNX option-path Identity output did not match the input."); }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            state = "executed",
            modelSha256 = Convert.ToHexString(SHA256.HashData(optionModel)).ToLowerInvariant(),
            inputName = parameter.Key,
            inputDimensions = parameter.Value.Lengths,
            outputDimensions = outputs.Single().Shape.Lengths,
            output,
            referenceMatched = optionReferenceMatched,
            options = new
            {
                defaultLoopIterations = 10,
                limitLoopIterations = ushort.MaxValue,
                externalDataPath = "model-directory-accepted-not-semantically-exercised",
                fastMath = true,
                exhaustiveTune = false,
            },
            evidence = "runtime-options-candidate-executed-review-required",
        }));
        return 0;
    }
    catch (Exception exception)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            state = "failed",
            evidence = "runtime-options-candidate-failed",
            exception = exception.GetType().FullName,
            exception.Message,
        }));
        return 1;
    }
}

if (args.Length != 3 || args[0] != "--fake-native" && args[0] != "--runtime-candidate")
{
    Console.Error.WriteLine("Usage: --fake-native|--runtime-candidate|--runtime-options-candidate <absolute-native-path> <absolute-model-path> | --expect-frontend-missing <absolute-native-path>");
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
