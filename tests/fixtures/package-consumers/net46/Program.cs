using JYPPX.ROCm.MIGraphXSharp;

internal static class Program
{
    private static int Main()
    {
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        var dynamicDimension = MIGraphXDynamicDimension.Range(1, 8, new long[] { 4 });
        var dynamicShape = MIGraphXShape.CreateDynamic(MIGraphXShapeDataType.Float32, new[] { dynamicDimension });
        var metadata = new MIGraphXCacheMetadata(
            new string('a', 64), "gpu", "offloadCopy=true", "msgpack", new string('b', 64),
            new[] { new MIGraphXCacheOverride("input", new[] { dynamicDimension }) });
        return MIGraphXBuildInfo.NativeBindingsAvailable
            && MIGraphXStatus.Success == 0
            && shape.ElementCount == 4
            && shape.ByteCount == 16
            && shape.IsPacked
            && dynamicShape.IsDynamic
            && dynamicShape.DynamicDimensions[0].Equals(dynamicDimension)
            && metadata.ComputeKey().Length == 64
            && typeof(MIGraphXProgram).Assembly == typeof(MIGraphXBuildInfo).Assembly ? 0 : 1;
    }
}
