using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

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

internal abstract class NativeCalibrationOptionsHandle : NativeOwnedHandle
{
    private readonly List<CalibrationSnapshot> calibration = new List<CalibrationSnapshot>();

    internal void AddCalibrationData(MIGraphXParameterMap data, Func<IntPtr, IntPtr, NativeMIGraphXStatus> add, string operation)
    {
        var snapshot = new CalibrationSnapshot(data.CloneForCalibration());
        try { calibration.Add(snapshot); }
        catch { snapshot.Release(); throw; }
        // A provider may store the copied argument pointers before returning a
        // failure. Retain the snapshot until this native option is destroyed.
        NativeStatus.ThrowIfFailed(add(handle, snapshot.Pointer), operation);
    }

    protected void RetainCalibrationFrom(NativeCalibrationOptionsHandle source)
    {
        foreach (var snapshot in source.calibration)
        {
            calibration.Add(snapshot);
            snapshot.Retain();
        }
    }

    protected abstract NativeMIGraphXStatus DestroyNative(IntPtr pointer);

    protected override bool ReleaseHandle()
    {
        try { DestroyNative(handle); return true; }
        finally
        {
            foreach (var snapshot in calibration) snapshot.Release();
            calibration.Clear();
        }
    }

    private sealed class CalibrationSnapshot
    {
        // The root keeps argument finalizers from freeing host buffers before
        // the option SafeHandle's critical finalizer destroys native references.
        private GCHandle root;
        private int references = 1;
        internal CalibrationSnapshot(MIGraphXParameterMap data)
        {
            try { root = GCHandle.Alloc(data); }
            catch { data.Dispose(); throw; }
        }
        internal IntPtr Pointer => ((MIGraphXParameterMap)root.Target!).Owner.HandleUnderLock;
        internal void Retain() => Interlocked.Increment(ref references);
        internal void Release()
        {
            if (Interlocked.Decrement(ref references) != 0) return;
            try { ((MIGraphXParameterMap)root.Target!).Dispose(); }
            finally { root.Free(); }
        }
    }
}

internal sealed class NativeQuantizeInt8OptionsHandle : NativeCalibrationOptionsHandle
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

    internal static NativeQuantizeInt8OptionsHandle CloneFrom(NativeQuantizeInt8OptionsHandle source)
    {
        var owned = Create();
        try
        {
            owned.RetainCalibrationFrom(source);
            NativeStatus.ThrowIfFailed(NativeMethods.QuantizeInt8OptionsAssignTo(owned.DangerousGetHandle(), source.DangerousGetHandle()), "migraphx_quantize_int8_options_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override NativeMIGraphXStatus DestroyNative(IntPtr pointer) => NativeMethods.QuantizeInt8OptionsDestroy(pointer);
}

internal sealed class NativeQuantizeFp8OptionsHandle : NativeCalibrationOptionsHandle
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

    internal static NativeQuantizeFp8OptionsHandle CloneFrom(NativeQuantizeFp8OptionsHandle source)
    {
        var owned = Create();
        try
        {
            owned.RetainCalibrationFrom(source);
            NativeStatus.ThrowIfFailed(NativeMethods.QuantizeFp8OptionsAssignTo(owned.DangerousGetHandle(), source.DangerousGetHandle()), "migraphx_quantize_fp8_options_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override NativeMIGraphXStatus DestroyNative(IntPtr pointer) => NativeMethods.QuantizeFp8OptionsDestroy(pointer);
}
