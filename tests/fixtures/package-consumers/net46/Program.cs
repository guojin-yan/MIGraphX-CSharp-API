using JYPPX.ROCm.MIGraphXSharp;

internal static class Program
{
    private static int Main() => MIGraphXBuildInfo.NativeBindingsAvailable ? 1 : 0;
}
