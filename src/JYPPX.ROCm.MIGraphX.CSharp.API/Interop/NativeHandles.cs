using System;
using Microsoft.Win32.SafeHandles;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeTargetHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private NativeTargetHandle()
        : base(true)
    {
    }

    internal static NativeTargetHandle Create(string name)
    {
        using (var utf8 = new StrictUtf8String(name, nameof(name)))
        {
            var status = NativeMethods.TargetCreate(out var raw, utf8.Pointer);
            var owned = new NativeTargetHandle();
            owned.SetHandle(raw);
            try
            {
                NativeStatus.ThrowIfFailed(status, "migraphx_target_create");
                if (owned.IsInvalid)
                {
                    throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, "migraphx_target_create (success with null handle)");
                }
                return owned;
            }
            catch
            {
                owned.Dispose();
                throw;
            }
        }
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.TargetDestroy(handle);
        return true;
    }
}

internal sealed class NativeProgramHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private NativeProgramHandle()
        : base(true)
    {
    }

    internal static NativeProgramHandle Create()
    {
        var status = NativeMethods.ProgramCreate(out var raw);
        var owned = new NativeProgramHandle();
        owned.SetHandle(raw);
        try
        {
            NativeStatus.ThrowIfFailed(status, "migraphx_program_create");
            if (owned.IsInvalid)
            {
                throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, "migraphx_program_create (success with null handle)");
            }
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeProgramHandle ParseFile(IntPtr path, NativeOnnxOptionsHandle options)
    {
        var status = NativeMethods.ParseOnnx(out var raw, path, options.DangerousGetHandle());
        return CompleteParsed(status, raw, "migraphx_parse_onnx");
    }

    internal static NativeProgramHandle ParseBuffer(IntPtr data, UIntPtr size, NativeOnnxOptionsHandle options)
    {
        var status = NativeMethods.ParseOnnxBuffer(out var raw, data, size, options.DangerousGetHandle());
        return CompleteParsed(status, raw, "migraphx_parse_onnx_buffer");
    }

    private static NativeProgramHandle CompleteParsed(NativeMIGraphXStatus status, IntPtr raw, string operation)
    {
        var owned = new NativeProgramHandle();
        owned.SetHandle(raw);
        try
        {
            NativeStatus.ThrowIfFailed(status, operation);
            if (owned.IsInvalid)
            {
                throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, $"{operation} (success with null handle)");
            }
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ProgramDestroy(handle);
        return true;
    }
}
