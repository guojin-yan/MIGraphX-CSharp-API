using JYPPX.ROCm.MIGraphXSharp;

if (args.Length != 2 || !Path.IsPathRooted(args[0]) || !Path.IsPathRooted(args[1]))
{
    Console.Error.WriteLine("Usage: M2OnnxIdentity <absolute-migraphx-c-path> <absolute-onnx-path>");
    return 2;
}

var input = new[] { 1f, -2f, 3.5f, 4f };
var result = MIGraphXOnnxWorkflow.RunFile(args[0], args[1], input);
Console.WriteLine($"input={result.InputName}; input-shape=[{string.Join(",", result.InputDimensions)}]; output-shape=[{string.Join(",", result.OutputDimensions)}]");
Console.WriteLine($"output=[{string.Join(",", result.Output)}]");
return result.Output.SequenceEqual(input) ? 0 : 1;
