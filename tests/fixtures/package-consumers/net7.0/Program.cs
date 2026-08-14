using JYPPX.ROCm.MIGraphXSharp;

internal static class Program
{
    private static int Main()
    {
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        return MIGraphXBuildInfo.NativeBindingsAvailable
            && MIGraphXStatus.Success == 0
            && shape.ElementCount == 4
            && shape.ByteCount == 16
            && shape.IsPacked
            && typeof(MIGraphXProgram).Assembly == typeof(MIGraphXBuildInfo).Assembly ? 0 : 1;
    }
}
