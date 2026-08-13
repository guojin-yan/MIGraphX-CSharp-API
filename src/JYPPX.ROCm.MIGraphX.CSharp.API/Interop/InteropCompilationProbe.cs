namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class InteropCompilationProbe
{
#if MIGRAPHX_LIBRARYIMPORT_PATH
    internal const string Strategy = "LibraryImport-planned";
#elif MIGRAPHX_DLLIMPORT_PATH
    internal const string Strategy = "DllImport-planned";
#else
#error A managed interop compilation path must be selected.
#endif
}
