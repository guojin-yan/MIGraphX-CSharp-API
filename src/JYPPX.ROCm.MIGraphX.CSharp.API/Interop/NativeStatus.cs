namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeStatus
{
    internal static void ThrowIfFailed(NativeMIGraphXStatus status, string operation)
    {
        if (status != NativeMIGraphXStatus.Success)
        {
            throw new MIGraphXException((int)status, operation);
        }
    }
}
