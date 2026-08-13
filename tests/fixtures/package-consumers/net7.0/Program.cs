using JYPPX.ROCm.MIGraphXSharp;

internal static class Program
{
    private static int Main() => MIGraphXBuildInfo.NativeBindingsAvailable
        && MIGraphXStatus.Success == 0
        && typeof(MIGraphXOnnxWorkflow).Assembly == typeof(MIGraphXBuildInfo).Assembly ? 0 : 1;
}
