using System;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeTfOptionsHandle : NativeOwnedHandle
{
    internal static NativeTfOptionsHandle Create()
    {
        var owned = OutHandle<NativeTfOptionsHandle>.Create("migraphx_tf_options_create");
        try
        {
            owned.Complete(NativeMethods.TfOptionsCreate(owned.OutSlot));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeTfOptionsHandle CloneFrom(IntPtr source)
    {
        var owned = Create();
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.TfOptionsAssignTo(owned.DangerousGetHandle(), source), "migraphx_tf_options_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.TfOptionsDestroy(handle); return true; }
}

internal sealed class NativeQuantizeOpNamesHandle : NativeOwnedHandle
{
    internal static NativeQuantizeOpNamesHandle Create()
    {
        var owned = OutHandle<NativeQuantizeOpNamesHandle>.Create("migraphx_quantize_op_names_create");
        try
        {
            owned.Complete(NativeMethods.QuantizeOpNamesCreate(owned.OutSlot));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeQuantizeOpNamesHandle CloneFrom(IntPtr source)
    {
        var owned = Create();
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.QuantizeOpNamesAssignTo(owned.DangerousGetHandle(), source), "migraphx_quantize_op_names_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.QuantizeOpNamesDestroy(handle); return true; }
}

internal sealed class NativeQuantizeInt8OptionsHandle : NativeOwnedHandle
{
    internal static NativeQuantizeInt8OptionsHandle Create()
    {
        var owned = OutHandle<NativeQuantizeInt8OptionsHandle>.Create("migraphx_quantize_int8_options_create");
        try
        {
            owned.Complete(NativeMethods.QuantizeInt8OptionsCreate(owned.OutSlot));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeQuantizeInt8OptionsHandle CloneFrom(IntPtr source)
    {
        var owned = Create();
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.QuantizeInt8OptionsAssignTo(owned.DangerousGetHandle(), source), "migraphx_quantize_int8_options_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.QuantizeInt8OptionsDestroy(handle); return true; }
}

internal sealed class NativeQuantizeFp8OptionsHandle : NativeOwnedHandle
{
    internal static NativeQuantizeFp8OptionsHandle Create()
    {
        var owned = OutHandle<NativeQuantizeFp8OptionsHandle>.Create("migraphx_quantize_fp8_options_create");
        try
        {
            owned.Complete(NativeMethods.QuantizeFp8OptionsCreate(owned.OutSlot));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeQuantizeFp8OptionsHandle CloneFrom(IntPtr source)
    {
        var owned = Create();
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.QuantizeFp8OptionsAssignTo(owned.DangerousGetHandle(), source), "migraphx_quantize_fp8_options_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.QuantizeFp8OptionsDestroy(handle); return true; }
}
