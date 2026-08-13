using System;
using System.Runtime.InteropServices;
using System.Text;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class StrictUtf8String : IDisposable
{
    private IntPtr pointer;

    internal StrictUtf8String(string value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }
        if (value.Length == 0)
        {
            throw new ArgumentException("The value must not be empty.", parameterName);
        }
        if (value.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("The value must not contain an embedded NUL character.", parameterName);
        }

        byte[] bytes;
        try
        {
            bytes = new UTF8Encoding(false, true).GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("The value contains invalid UTF-16 and cannot be encoded as strict UTF-8.", parameterName, exception);
        }

        pointer = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        Marshal.WriteByte(pointer, bytes.Length, 0);
    }

    internal IntPtr Pointer => pointer;

    public void Dispose()
    {
        var value = pointer;
        pointer = IntPtr.Zero;
        if (value != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(value);
        }
    }
}
