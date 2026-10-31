namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeAsyncMethods
{
    internal static readonly string[] AdditionalRequiredExports =
    {
        "migraphx_program_run_async",
    };
}
