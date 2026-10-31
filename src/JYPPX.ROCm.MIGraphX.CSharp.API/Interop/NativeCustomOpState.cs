using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

// The provider copies this state at creation and again into its registry and
// operation values. Track every native copy, without rooting the managed owner.
internal static class NativeCustomOpState
{
    private static readonly object Sync = new object();
    private static readonly Dictionary<IntPtr, Entry> Entries = new Dictionary<IntPtr, Entry>();

    internal static int LiveCount { get { lock (Sync) return Entries.Count; } }

    internal static Input Create(object state, string typeName, NativeCustomOpCallbackRoots callbacks)
    {
        var name = new SharedTypeName(typeName);
        try
        {
            var construction = new Construction();
            var entry = new Entry(name, new WeakReference(callbacks), null, construction);
            lock (Sync) return new Input(Allocate(state, entry), name.Pointer, construction);
        }
        catch { name.DisposeIfUnreferenced(); throw; }
    }

    internal static NativeMIGraphXStatus Copy(IntPtr output, IntPtr input)
    {
        var pointer = IntPtr.Zero;
        try
        {
            if (output == IntPtr.Zero || input == IntPtr.Zero) return NativeMIGraphXStatus.BadParameter;
            Marshal.WriteIntPtr(output, IntPtr.Zero);
            lock (Sync)
            {
                if (!Entries.TryGetValue(input, out var source)) return NativeMIGraphXStatus.BadParameter;
                var roots = source.RetainedCallbacks ?? source.Callbacks.Target as NativeCustomOpCallbackRoots;
                var retained = source.Construction.Active ? null : roots?.Snapshot();
                pointer = Allocate(GCHandle.FromIntPtr(input).Target!, new Entry(source.TypeName, source.Callbacks, retained, source.Construction));
            }
            Marshal.WriteIntPtr(output, pointer);
            return NativeMIGraphXStatus.Success;
        }
        catch
        {
            if (pointer != IntPtr.Zero) Delete(pointer);
            return NativeMIGraphXStatus.UnknownError;
        }
    }

    internal static NativeMIGraphXStatus Delete(IntPtr pointer)
    {
        try
        {
            lock (Sync)
            {
                if (!Entries.TryGetValue(pointer, out var entry)) return NativeMIGraphXStatus.BadParameter;
                Entries.Remove(pointer);
                try { GCHandle.FromIntPtr(pointer).Free(); }
                finally { entry.TypeName.Release(); }
            }
            return NativeMIGraphXStatus.Success;
        }
        catch { return NativeMIGraphXStatus.UnknownError; }
    }

    private static IntPtr Allocate(object state, Entry entry)
    {
        var root = GCHandle.Alloc(state);
        try
        {
            var pointer = GCHandle.ToIntPtr(root);
            Entries.Add(pointer, entry);
            entry.TypeName.Retain();
            return pointer;
        }
        catch { root.Free(); throw; }
    }

    internal sealed class Construction { internal bool Active = true; }

    internal sealed class Input : IDisposable
    {
        private IntPtr pointer;
        private readonly Construction construction;
        internal Input(IntPtr pointer, IntPtr typeName, Construction construction) { this.pointer = pointer; TypeName = typeName; this.construction = construction; }
        internal IntPtr Pointer => pointer;
        internal IntPtr TypeName { get; }
        public void Dispose()
        {
            lock (Sync)
            {
                construction.Active = false;
                var owned = Interlocked.Exchange(ref pointer, IntPtr.Zero);
                if (owned != IntPtr.Zero) Delete(owned);
            }
        }
    }

    private sealed class Entry
    {
        internal Entry(SharedTypeName typeName, WeakReference callbacks, NativeCustomOpCallbackRoots? retainedCallbacks, Construction construction)
        { TypeName = typeName; Callbacks = callbacks; RetainedCallbacks = retainedCallbacks; Construction = construction; }
        internal SharedTypeName TypeName { get; }
        internal WeakReference Callbacks { get; }
        internal NativeCustomOpCallbackRoots? RetainedCallbacks { get; }
        internal Construction Construction { get; }
    }

    private sealed class SharedTypeName
    {
        private readonly StrictUtf8String value;
        private int references;
        internal SharedTypeName(string name) => value = new StrictUtf8String(name, "objectTypeName");
        internal IntPtr Pointer => value.Pointer;
        internal void Retain() => references++;
        internal void Release() { if (--references == 0) value.Dispose(); }
        internal void DisposeIfUnreferenced() { if (references == 0) value.Dispose(); }
    }
}

internal sealed class NativeCustomOpCallbackRoots
{
    internal object Sync { get; } = new object();
    internal Delegate? Compute;
    internal Delegate? ComputeShape;
    internal Delegate? OutputAlias;
    internal Delegate? RunsOnOffloadTarget;
    internal NativeCustomOpCallbackRoots Snapshot()
    {
        lock (Sync) return new NativeCustomOpCallbackRoots { Compute = Compute, ComputeShape = ComputeShape, OutputAlias = OutputAlias, RunsOnOffloadTarget = RunsOnOffloadTarget };
    }
    internal void Clear() { Compute = null; ComputeShape = null; OutputAlias = null; RunsOnOffloadTarget = null; }
}
