using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using JYPPX.ROCm.MIGraphXSharp;

internal static class Program
{
    private static int Main()
    {
        // Type loading is intentionally the only runtime operation. Native HIP
        // and MIGraphX entry points are not touched by this clean-consumer smoke.
        var surface =
            typeof(Consumer) != null
            && typeof(MIGraphXHipAsyncRun) != null
            && typeof(MIGraphXHipDeviceInput) != null
            && typeof(HipDeviceMemory) != null
            && typeof(HipStream) != null;
        return surface && typeof(MIGraphXBuildInfo).Assembly != typeof(HipStream).Assembly ? 0 : 1;
    }
}

