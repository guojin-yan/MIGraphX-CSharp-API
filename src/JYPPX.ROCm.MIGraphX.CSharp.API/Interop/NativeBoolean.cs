using System;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeBoolean
{
    internal static bool Read(IntPtr output, string operation) => Read(Marshal.ReadByte(output), operation);

    internal static bool Read(byte value, string operation)
    {
        if (value > 1)
        {
            throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, $"{operation} (success with invalid C bool {value})");
        }
        return value == 1;
    }
}
