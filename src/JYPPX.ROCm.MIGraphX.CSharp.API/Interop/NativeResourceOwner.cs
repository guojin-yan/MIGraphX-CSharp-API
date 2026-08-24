using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeResourceOwner<THandle> : IDisposable
    where THandle : NativeOwnedHandle
{
    private readonly object sync = new object();
    private THandle? handle;

    internal NativeResourceOwner(NativeRuntime runtime, THandle handle)
    {
        Runtime = runtime;
        this.handle = handle;
        Id = NativeResourceIds.Next();
    }

    internal NativeRuntime Runtime { get; }

    internal long Id { get; }

    internal object Sync => sync;

    internal IntPtr HandleUnderLock
    {
        get
        {
            if (handle is null || handle.IsClosed || handle.IsInvalid)
            {
                throw new ObjectDisposedException(typeof(THandle).Name);
            }

            return handle.DangerousGetHandle();
        }
    }

    internal TResult WithHandle<TResult>(Func<IntPtr, TResult> action)
    {
        lock (sync)
        {
            return action(HandleUnderLock);
        }
    }

    internal void WithHandle(Action<IntPtr> action)
    {
        lock (sync)
        {
            action(HandleUnderLock);
        }
    }

    internal NativeHandleLease AcquireLease()
    {
        lock (sync)
        {
            var owned = handle;
            _ = HandleUnderLock;
            var addedReference = false;
            try
            {
                owned!.DangerousAddRef(ref addedReference);
                return new NativeHandleLease(owned, owned.DangerousGetHandle(), addedReference);
            }
            catch
            {
                if (addedReference) owned!.DangerousRelease();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            var owned = handle;
            handle = null;
            owned?.Dispose();
        }
    }
}

internal sealed class NativeHandleLease : IDisposable
{
    private NativeOwnedHandle? handle;

    internal NativeHandleLease(NativeOwnedHandle handle, IntPtr pointer, bool addedReference)
    {
        this.handle = addedReference ? handle : null;
        Pointer = pointer;
    }

    internal IntPtr Pointer { get; }

    internal NativeHandleLease Duplicate()
    {
        var owned = handle ?? throw new ObjectDisposedException(nameof(NativeHandleLease));
        var addedReference = false;
        try
        {
            owned.DangerousAddRef(ref addedReference);
            return new NativeHandleLease(owned, owned.DangerousGetHandle(), addedReference);
        }
        catch
        {
            if (addedReference) owned.DangerousRelease();
            throw;
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref handle, null)?.DangerousRelease();
    }
}

internal sealed class NativeLeaseSet : IDisposable
{
    private IDisposable[]? leases;

    internal NativeLeaseSet(IEnumerable<IDisposable> leases) => this.leases = leases.ToArray();

    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref leases, null);
        if (owned is null) return;
        for (var index = owned.Length - 1; index >= 0; index--)
        {
            try { owned[index].Dispose(); }
            catch { }
        }
    }
}

internal static class NativeResourceIds
{
    private static long nextId;

    internal static long Next() => Interlocked.Increment(ref nextId);
}

internal static class NativeResourceLock
{
    internal static NativeResourceLockTarget Target(long id, object sync) => new NativeResourceLockTarget(id, sync);

    internal static TResult With<TResult>(IReadOnlyList<NativeResourceLockTarget> resources, Func<TResult> action)
    {
        var ordered = resources
            .GroupBy(resource => resource.Id)
            .Select(group => group.First())
            .OrderBy(resource => resource.Id)
            .ToArray();
        return Enter(ordered, 0, action);
    }

    internal static void With(IReadOnlyList<NativeResourceLockTarget> resources, Action action)
    {
        With(resources, () => { action(); return 0; });
    }

    private static TResult Enter<TResult>(NativeResourceLockTarget[] resources, int index, Func<TResult> action)
    {
        if (index == resources.Length)
        {
            return action();
        }

        lock (resources[index].Sync)
        {
            return Enter(resources, index + 1, action);
        }
    }
}

internal sealed class NativeResourceLockTarget
{
    internal NativeResourceLockTarget(long id, object sync)
    {
        Id = id;
        Sync = sync;
    }

    internal long Id { get; }

    internal object Sync { get; }
}
