using JYPPX.ROCm.MIGraphXSharp;

internal static class Program
{
    private static int Main() => MIGraphXBuildInfo.NativeBindingsAvailable && MIGraphXStatus.Success == 0 ? 0 : 1;
}
