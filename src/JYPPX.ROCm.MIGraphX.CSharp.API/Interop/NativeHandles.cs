using System;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeTargetHandle : NativeOwnedHandle
{
    private NativeTargetHandle()
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

    internal static NativeTargetHandle CloneFrom(IntPtr source, string name)
    {
        var owned = Create(name);
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.TargetAssignTo(owned.DangerousGetHandle(), source), "migraphx_target_assign_to");
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
        NativeMethods.TargetDestroy(handle);
        return true;
    }
}

internal sealed class NativeProgramHandle : NativeOwnedHandle
{
    private NativeProgramHandle()
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

    internal static NativeProgramHandle CloneFrom(IntPtr source)
    {
        var owned = Create();
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.ProgramAssignTo(owned.DangerousGetHandle(), source), "migraphx_program_assign_to");
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

    internal static NativeProgramHandle ParseFile(IntPtr path, IntPtr options)
    {
        var status = NativeMethods.ParseOnnx(out var raw, path, options);
        return CompleteParsed(status, raw, "migraphx_parse_onnx");
    }

    internal static NativeProgramHandle ParseBuffer(IntPtr data, UIntPtr size, NativeOnnxOptionsHandle options)
    {
        var status = NativeMethods.ParseOnnxBuffer(out var raw, data, size, options.DangerousGetHandle());
        return CompleteParsed(status, raw, "migraphx_parse_onnx_buffer");
    }

    internal static NativeProgramHandle ParseBuffer(IntPtr data, UIntPtr size, IntPtr options)
    {
        var status = NativeMethods.ParseOnnxBuffer(out var raw, data, size, options);
        return CompleteParsed(status, raw, "migraphx_parse_onnx_buffer");
    }

    internal static NativeProgramHandle ParseTfFile(IntPtr path, NativeTfOptionsHandle options)
    {
        var owned = OutHandle<NativeProgramHandle>.Create("migraphx_parse_tf");
        try
        {
            owned.Complete(NativeMethods.ParseTf(owned.OutSlot, path, options.DangerousGetHandle()));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeProgramHandle ParseTfFile(IntPtr path, IntPtr options)
    {
        var owned = OutHandle<NativeProgramHandle>.Create("migraphx_parse_tf");
        try
        {
            owned.Complete(NativeMethods.ParseTf(owned.OutSlot, path, options));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeProgramHandle ParseTfBuffer(IntPtr data, UIntPtr size, NativeTfOptionsHandle options)
    {
        var owned = OutHandle<NativeProgramHandle>.Create("migraphx_parse_tf_buffer");
        try
        {
            owned.Complete(NativeMethods.ParseTfBuffer(owned.OutSlot, data, size, options.DangerousGetHandle()));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeProgramHandle ParseTfBuffer(IntPtr data, UIntPtr size, IntPtr options)
    {
        var owned = OutHandle<NativeProgramHandle>.Create("migraphx_parse_tf_buffer");
        try
        {
            owned.Complete(NativeMethods.ParseTfBuffer(owned.OutSlot, data, size, options));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeProgramHandle Load(IntPtr path, IntPtr options)
    {
        var slot = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(slot, IntPtr.Zero);
            var status = NativeMethods.Load(slot, path, options);
            var raw = Marshal.ReadIntPtr(slot);
            var owned = new NativeProgramHandle();
            owned.Initialize(raw);
            return NativeHandleFactory.CompleteCreate(owned, status, "migraphx_load");
        }
        finally { Marshal.FreeHGlobal(slot); }
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
