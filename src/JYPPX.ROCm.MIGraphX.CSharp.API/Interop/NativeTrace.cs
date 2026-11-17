using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeTrace
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeMIGraphXStatus TraceCallback(IntPtr info, IntPtr data);

    internal static NativeArgumentsHandle Run(IntPtr program, IntPtr parameters, List<MIGraphXTraceInfo> snapshots)
    {
        ExceptionDispatchInfo? failure = null;
        var sync = new object();
        TraceCallback callback = (info, _) =>
        {
            // Never allow a managed exception to cross a native callback frame.
            lock (sync)
            {
                if (failure is not null) return NativeMIGraphXStatus.UnknownError;
                try { snapshots.Add(MIGraphXTraceInfo.Snapshot(info)); return NativeMIGraphXStatus.Success; }
                catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); return NativeMIGraphXStatus.UnknownError; }
            }
        };
        var output = OutHandle<NativeArgumentsHandle>.Create("migraphx_program_run_trace");
        try
        {
            var status = NativeMethods.ProgramRunTrace(output.OutSlot, program, parameters, Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero);
            try { output.Complete(status); }
            catch { failure?.Throw(); throw; }
            failure?.Throw();
            return output.Handle;
        }
        catch { output.Dispose(); throw; }
        finally { GC.KeepAlive(callback); }
    }
}
