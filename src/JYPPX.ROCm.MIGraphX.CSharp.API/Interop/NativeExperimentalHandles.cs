using System;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeExperimentalCustomOpHandle : NativeOwnedHandle
{
    internal static NativeExperimentalCustomOpHandle Create(
        IntPtr obj,
        IntPtr copy,
        IntPtr delete,
        IntPtr objectTypeName,
        IntPtr name)
    {
        var slot = System.Runtime.InteropServices.Marshal.AllocHGlobal(IntPtr.Size);
        System.Runtime.InteropServices.Marshal.WriteIntPtr(slot, IntPtr.Zero);
        NativeExperimentalCustomOpHandle? owned = null;
        try
        {
            var status = NativeMethods.ExperimentalCustomOpCreate(slot, obj, copy, delete, objectTypeName, name);
            var raw = System.Runtime.InteropServices.Marshal.ReadIntPtr(slot);
            owned = new NativeExperimentalCustomOpHandle();
            owned.Initialize(raw);
            return NativeHandleFactory.CompleteCreate(owned, status, "migraphx_experimental_custom_op_create");
        }
        catch
        {
            if (owned is null)
            {
                var raw = System.Runtime.InteropServices.Marshal.ReadIntPtr(slot);
                if (raw != IntPtr.Zero)
                {
                    using (var abandoned = new NativeExperimentalCustomOpHandle())
                    {
                        abandoned.Initialize(raw);
                    }
                }
            }
            throw;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(slot);
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.ExperimentalCustomOpDestroy(handle); return true; }
}
