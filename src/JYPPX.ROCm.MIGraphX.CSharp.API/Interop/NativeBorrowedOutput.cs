using System;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeBorrowedOutput
{
    internal static IntPtr RequireHandle(IntPtr value, string operation)
    {
        if (value == IntPtr.Zero)
        {
            throw new MIGraphXException(
                (int)NativeMIGraphXStatus.UnknownError,
                $"{operation} (success with null borrowed handle)");
        }
        return value;
    }
}
