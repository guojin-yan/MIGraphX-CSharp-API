namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class InteropCompilationProbe
{
#if MIGRAPHX_LIBRARYIMPORT_PATH
    internal const string Strategy = "LibraryImport";
#elif MIGRAPHX_DLLIMPORT_PATH
    internal const string Strategy = "DllImport";
#else
#error A managed interop compilation path must be selected.
#endif
}
