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

    internal void InvalidateCompilation()
    {
        IsCompiled = false;
        Revision++;
    }
}
