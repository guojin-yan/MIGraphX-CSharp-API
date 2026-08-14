using System.Collections.Generic;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;
using JYPPX.ROCm.MIGraphXSharp;

internal static class Consumer
{
    internal static MIGraphXHipAsyncRun Host(MIGraphXProgram program, MIGraphXParameterMap parameters, HipStream stream)
        => program.RunHostAsync(parameters, stream);

    internal static MIGraphXHipAsyncRun Device(
        MIGraphXProgram program,
        string name,
        MIGraphXShape shape,
        HipDeviceMemory memory,
        HipStream stream)
        => program.RunDeviceAsync(new List<MIGraphXHipDeviceInput> { new MIGraphXHipDeviceInput(name, shape, memory) }, stream);
}
