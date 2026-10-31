using System;
using System.Threading;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

// Shared by the program and its borrowed graph/context views. Native ownership
// stays with their SafeHandle leases; this object contains no native pointer.
internal sealed class NativeProgramState
{
    internal NativeProgramState(long id, object sync) { Id = id; Sync = sync; }

    internal long Id { get; }
    internal object Sync { get; }
    internal bool IsCompiled { get; set; }
    internal bool OffloadCopy { get; set; }
    internal long Revision { get; private set; }
    private int pendingRuns;

    internal void RequireIdle()
    {
        if (pendingRuns != 0)
            throw new InvalidOperationException("Complete all asynchronous runs on this program before modifying it, copying it or running synchronously.");
    }

    internal IDisposable AcquireAsyncLease()
    {
        lock (Sync)
        {
            var lease = new PendingRunLease(this);
            pendingRuns = checked(pendingRuns + 1);
            return lease;
        }
    }

    internal void InvalidateCompilation()
    {
        RequireIdle();
        IsCompiled = false;
        Revision++;
    }

    private sealed class PendingRunLease : IDisposable
    {
        private NativeProgramState? state;
        internal PendingRunLease(NativeProgramState state) => this.state = state;
        public void Dispose()
        {
            var retained = Interlocked.Exchange(ref state, null);
            if (retained is null) return;
            lock (retained.Sync) retained.pendingRuns--;
        }
    }
}
