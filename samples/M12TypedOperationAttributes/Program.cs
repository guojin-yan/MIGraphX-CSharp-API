using JYPPX.ROCm.MIGraphXSharp;

var operations = new (string Name, MIGraphXOperationAttributes Attributes)[]
{
    ("reshape", MIGraphXOperationAttributes.ForReshape(1, 4)),
    ("transpose", MIGraphXOperationAttributes.ForTranspose(1, 0)),
    ("slice", MIGraphXOperationAttributes.ForSlice(new long[] { 0 }, new long[] { 0 }, new long[] { 1 })),
    ("multibroadcast", MIGraphXOperationAttributes.ForMultibroadcast(1, 4)),
    ("topk", MIGraphXOperationAttributes.ForTopK(1, 1, true))
};

foreach (var operation in operations)
{
    Console.WriteLine($"{operation.Name}: {operation.Attributes.Build()}");
}

if (args.Length == 0)
{
    Console.WriteLine("No native library supplied; managed attribute materialization complete.");
    return 0;
}

if (args.Length != 1 || !Path.IsPathRooted(args[0]))
{
    Console.Error.WriteLine("Usage: M12TypedOperationAttributes [absolute-migraphx-c-path]");
    return 2;
}

foreach (var operation in operations)
{
    using var created = MIGraphXOperation.Create(args[0], operation.Name, operation.Attributes);
    using var clone = created.Clone();
    if (created.Name != operation.Name || clone.Name != operation.Name)
    {
        Console.Error.WriteLine($"Operation name mismatch: {operation.Name}");
        return 1;
    }
    Console.WriteLine($"{operation.Name}: native create/clone succeeded");
}

return 0;
