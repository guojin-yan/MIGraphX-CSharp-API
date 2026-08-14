using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeOptimalsHandle : NativeOwnedHandle
{
    internal static NativeOptimalsHandle Create(IReadOnlyList<long> values)
    {
        var valueBuffer = NativeSizeTArray.Alloc(values, nameof(values));
        try
        {
            var owned = CreateEmpty("migraphx_optimals_create");
            try
            {
                var status = NativeMethods.OptimalsCreate(owned.OutSlot, valueBuffer.Pointer, NativeSizeTArray.Count(values.Count));
                owned.Complete(status);
                return owned.Handle;
            }
            catch
            {
                owned.Dispose();
                throw;
            }
        }
        finally { valueBuffer.Dispose(); }
    }

    protected override bool ReleaseHandle() { NativeMethods.OptimalsDestroy(handle); return true; }

    private static OutHandle<NativeOptimalsHandle> CreateEmpty(string operation) => OutHandle<NativeOptimalsHandle>.Create(operation);
}

internal sealed class NativeDynamicDimensionHandle : NativeOwnedHandle
{
    internal static NativeDynamicDimensionHandle Create(MIGraphXDynamicDimension value)
    {
        if (value is null) { throw new ArgumentNullException(nameof(value)); }
        var owned = OutHandle<NativeDynamicDimensionHandle>.Create("migraphx_dynamic_dimension_create_min_max");
        try
        {
            NativeMIGraphXStatus status;
            if (value.Optimals.Count == 0)
            {
                status = NativeMethods.DynamicDimensionCreateMinMax(
                    owned.OutSlot,
                    MIGraphXDynamicDimension.ToNativeSizeT(value.Minimum, nameof(value.Minimum)),
                    MIGraphXDynamicDimension.ToNativeSizeT(value.Maximum, nameof(value.Maximum)));
            }
            else
            {
                using (var optimals = NativeOptimalsHandle.Create(value.Optimals))
                {
                    status = NativeMethods.DynamicDimensionCreateMinMaxOptimals(
                        owned.OutSlot,
                        MIGraphXDynamicDimension.ToNativeSizeT(value.Minimum, nameof(value.Minimum)),
                        MIGraphXDynamicDimension.ToNativeSizeT(value.Maximum, nameof(value.Maximum)),
                        optimals.DangerousGetHandle());
                }
            }
            owned.Complete(status);
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal bool IsFixedValue()
    {
        var output = Marshal.AllocHGlobal(1);
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.DynamicDimensionIsFixed(output, DangerousGetHandle()), "migraphx_dynamic_dimension_is_fixed");
            return Marshal.ReadByte(output) != 0;
        }
        finally { Marshal.FreeHGlobal(output); }
    }

    internal bool EqualsValue(NativeDynamicDimensionHandle other)
    {
        if (other is null) { throw new ArgumentNullException(nameof(other)); }
        var output = Marshal.AllocHGlobal(1);
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.DynamicDimensionEqual(output, DangerousGetHandle(), other.DangerousGetHandle()), "migraphx_dynamic_dimension_equal");
            return Marshal.ReadByte(output) != 0;
        }
        finally { Marshal.FreeHGlobal(output); }
    }

    protected override bool ReleaseHandle() { NativeMethods.DynamicDimensionDestroy(handle); return true; }
}

internal sealed class NativeDynamicDimensionsHandle : NativeOwnedHandle
{
    internal static NativeDynamicDimensionsHandle FromShape(IntPtr shape)
    {
        var owned = OutHandle<NativeDynamicDimensionsHandle>.Create("migraphx_shape_dyn_dims");
        try
        {
            var status = NativeMethods.ShapeDynDims(owned.OutSlot, shape);
            owned.Complete(status);
            return owned.Handle;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    internal static NativeDynamicDimensionsHandle Create(IReadOnlyList<MIGraphXDynamicDimension> values)
    {
        var dimensions = new List<NativeDynamicDimensionHandle>(values.Count);
        try
        {
            for (var index = 0; index < values.Count; index++) { dimensions.Add(NativeDynamicDimensionHandle.Create(values[index])); }
            var pointerBytes = checked(values.Count * IntPtr.Size);
            var pointers = pointerBytes == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(pointerBytes);
            try
            {
                for (var index = 0; index < dimensions.Count; index++) { Marshal.WriteIntPtr(pointers, index * IntPtr.Size, dimensions[index].DangerousGetHandle()); }
                var owned = OutHandle<NativeDynamicDimensionsHandle>.Create("migraphx_dynamic_dimensions_create");
                try
                {
                    var status = NativeMethods.DynamicDimensionsCreate(owned.OutSlot, pointers, NativeSizeTArray.Count(values.Count));
                    owned.Complete(status);
                    return owned.Handle;
                }
                catch
                {
                    owned.Dispose();
                    throw;
                }
            }
            finally { if (pointers != IntPtr.Zero) { Marshal.FreeHGlobal(pointers); } }
        }
        finally
        {
            foreach (var dimension in dimensions) { dimension.Dispose(); }
        }
    }

    internal bool[] ReadFixedFlags()
    {
        var count = ReadCount();
        var flags = new bool[count];
        for (var index = 0; index < count; index++)
        {
            var slot = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(slot, IntPtr.Zero);
                NativeStatus.ThrowIfFailed(NativeMethods.DynamicDimensionsGet(slot, DangerousGetHandle(), NativeSizeTArray.Count(index)), "migraphx_dynamic_dimensions_get");
                var borrowed = Marshal.ReadIntPtr(slot);
                if (borrowed == IntPtr.Zero) { throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, "migraphx_dynamic_dimensions_get (success with null borrowed handle)"); }
                var probe = new NativeDynamicDimensionBorrowed(borrowed);
                flags[index] = probe.IsFixedValue();
            }
            finally { Marshal.FreeHGlobal(slot); }
        }
        var stableCount = ReadCount();
        if (stableCount != count) { throw new InvalidOperationException($"Native dynamic dimension collection changed from {count} to {stableCount} while creating a snapshot."); }
        return flags;
    }

    private int ReadCount()
    {
        var countSlot = Marshal.AllocHGlobal(UIntPtr.Size);
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.DynamicDimensionsSize(countSlot, DangerousGetHandle()), "migraphx_dynamic_dimensions_size");
            var countValue = UIntPtr.Size == 8 ? new UIntPtr(unchecked((ulong)Marshal.ReadInt64(countSlot))) : new UIntPtr(unchecked((uint)Marshal.ReadInt32(countSlot)));
            return NativeShapeSnapshot.ToInt(countValue, "dynamic dimension count");
        }
        finally { Marshal.FreeHGlobal(countSlot); }
    }

    protected override bool ReleaseHandle() { NativeMethods.DynamicDimensionsDestroy(handle); return true; }

    private sealed class NativeDynamicDimensionBorrowed
    {
        private readonly IntPtr handle;
        internal NativeDynamicDimensionBorrowed(IntPtr handle) { this.handle = handle; }
        internal bool IsFixedValue()
        {
            var output = Marshal.AllocHGlobal(1);
            try
            {
                NativeStatus.ThrowIfFailed(NativeMethods.DynamicDimensionIsFixed(output, handle), "migraphx_dynamic_dimension_is_fixed");
                return Marshal.ReadByte(output) != 0;
            }
            finally { Marshal.FreeHGlobal(output); }
        }
    }
}

internal sealed class NativeFileOptionsHandle : NativeOwnedHandle
{
    internal static NativeFileOptionsHandle Create(string format)
    {
        using (var utf8 = new StrictUtf8String(format, nameof(format)))
        {
            var owned = OutHandle<NativeFileOptionsHandle>.Create("migraphx_file_options_create");
            try
            {
                var status = NativeMethods.FileOptionsCreate(owned.OutSlot);
                owned.Complete(status);
                NativeStatus.ThrowIfFailed(NativeMethods.FileOptionsSetFileFormat(owned.Handle.DangerousGetHandle(), utf8.Pointer), "migraphx_file_options_set_file_format");
                return owned.Handle;
            }
            catch
            {
                owned.Dispose();
                throw;
            }
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.FileOptionsDestroy(handle); return true; }
}

internal sealed class NativeSizeTArray : IDisposable
{
    private NativeSizeTArray(IntPtr pointer, int count) { Pointer = pointer; CountValue = count; }
    internal IntPtr Pointer { get; }
    internal int CountValue { get; }

    internal static NativeSizeTArray Alloc(IReadOnlyList<long> values, string parameterName)
    {
        var bytes = checked(values.Count * UIntPtr.Size);
        var pointer = bytes == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(bytes);
        try
        {
            for (var index = 0; index < values.Count; index++)
            {
                var native = MIGraphXDynamicDimension.ToNativeSizeT(values[index], parameterName);
                if (UIntPtr.Size == 8) { Marshal.WriteInt64(pointer, index * UIntPtr.Size, unchecked((long)native.ToUInt64())); }
                else { Marshal.WriteInt32(pointer, index * UIntPtr.Size, unchecked((int)native.ToUInt32())); }
            }
            return new NativeSizeTArray(pointer, values.Count);
        }
        catch
        {
            if (pointer != IntPtr.Zero) { Marshal.FreeHGlobal(pointer); }
            throw;
        }
    }

    internal static UIntPtr Count(int count) => UIntPtr.Size == 4 ? new UIntPtr((uint)count) : new UIntPtr((ulong)count);
    public void Dispose() { if (Pointer != IntPtr.Zero) { Marshal.FreeHGlobal(Pointer); } }
}

internal sealed class OutHandle<T> : IDisposable where T : NativeOwnedHandle
{
    private IntPtr slot;
    private OutHandle(string operation)
    {
        slot = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(slot, IntPtr.Zero);
        OutSlot = slot;
        Operation = operation;
    }

    internal IntPtr OutSlot { get; }
    internal string Operation { get; }
    internal T Handle { get; private set; } = null!;

    internal static OutHandle<T> Create(string operation) => new OutHandle<T>(operation);

    internal void Complete(NativeMIGraphXStatus status)
    {
        var raw = Marshal.ReadIntPtr(slot);
        Marshal.FreeHGlobal(slot);
        slot = IntPtr.Zero;
        var handle = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
        handle.Initialize(raw);
        Handle = NativeHandleFactory.CompleteCreate(handle, status, Operation);
    }

    public void Dispose()
    {
        if (slot != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(slot);
            slot = IntPtr.Zero;
        }
        if (Handle is not null)
        {
            Handle.Dispose();
            Handle = null!;
        }
    }
}
