using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeInstructionHandle : NativeOwnedHandle
{
    internal static NativeInstructionHandle FromOut(IntPtr slot, string operation)
    {
        var raw = Marshal.ReadIntPtr(slot);
        var owned = new NativeInstructionHandle();
        owned.Initialize(raw);
        return NativeHandleFactory.CompleteCreate(owned, NativeMIGraphXStatus.Success, operation);
    }

    protected override bool ReleaseHandle() { NativeMethods.InstructionDestroy(handle); return true; }
}

internal sealed class NativeInstructionsHandle : NativeOwnedHandle
{
    internal static NativeInstructionsHandle Create(IReadOnlyList<IntPtr> pointers)
    {
        if (pointers is null) throw new ArgumentNullException(nameof(pointers));
        var bytes = checked(pointers.Count * IntPtr.Size);
        var buffer = bytes == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(bytes);
        try
        {
            for (var index = 0; index < pointers.Count; index++) Marshal.WriteIntPtr(buffer, index * IntPtr.Size, pointers[index]);
            var owned = OutHandle<NativeInstructionsHandle>.Create("migraphx_instructions_create");
            try
            {
                owned.Complete(NativeMethods.InstructionsCreate(owned.OutSlot, buffer, NativeSizeTArray.Count(pointers.Count)));
                return owned.Handle;
            }
            catch
            {
                owned.Dispose();
                throw;
            }
        }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }

    internal static NativeInstructionsHandle CloneFrom(IntPtr source, IReadOnlyList<IntPtr> pointers)
    {
        var owned = Create(pointers);
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.InstructionsAssignTo(owned.DangerousGetHandle(), source), "migraphx_instructions_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.InstructionsDestroy(handle); return true; }
}

internal sealed class NativeModulesHandle : NativeOwnedHandle
{
    internal static NativeModulesHandle Create(IReadOnlyList<IntPtr> pointers)
    {
        if (pointers is null) throw new ArgumentNullException(nameof(pointers));
        var bytes = checked(pointers.Count * IntPtr.Size);
        var buffer = bytes == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(bytes);
        try
        {
            for (var index = 0; index < pointers.Count; index++) Marshal.WriteIntPtr(buffer, index * IntPtr.Size, pointers[index]);
            var owned = OutHandle<NativeModulesHandle>.Create("migraphx_modules_create");
            try
            {
                owned.Complete(NativeMethods.ModulesCreate(owned.OutSlot, buffer, NativeSizeTArray.Count(pointers.Count)));
                return owned.Handle;
            }
            catch
            {
                owned.Dispose();
                throw;
            }
        }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }

    internal static NativeModulesHandle CloneFrom(IntPtr source, IReadOnlyList<IntPtr> pointers)
    {
        var owned = Create(pointers);
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.ModulesAssignTo(owned.DangerousGetHandle(), source), "migraphx_modules_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.ModulesDestroy(handle); return true; }
}

internal sealed class NativeOperationHandle : NativeOwnedHandle
{
    internal static NativeOperationHandle CreateNoAttributes(IntPtr name)
    {
        return CreateCore(name, IntPtr.Zero, NativeMethods.OperationCreateNoAttributes);
    }

    internal static NativeOperationHandle CreateWithMaterializedAttributes(IntPtr name, IntPtr attributes)
    {
        return CreateCore(name, attributes, NativeMethods.OperationCreateMaterializedAttributes);
    }

    private static NativeOperationHandle CreateCore(
        IntPtr name,
        IntPtr attributes,
        Func<IntPtr, IntPtr, IntPtr, NativeMIGraphXStatus> create)
    {
        var owned = OutHandle<NativeOperationHandle>.Create("migraphx_operation_create");
        try
        {
            owned.Complete(create(owned.OutSlot, name, attributes));
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeOperationHandle CloneFrom(IntPtr source, IntPtr name)
    {
        var owned = CreateNoAttributes(name);
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.OperationAssignTo(owned.DangerousGetHandle(), source), "migraphx_operation_assign_to");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.OperationDestroy(handle); return true; }
}
