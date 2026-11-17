using System;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>独立于回调生命周期的执行跟踪元数据，不解引用 GPU 结果数据。 Execution trace metadata independent of callback lifetime; GPU result data is never dereferenced.</summary>
public sealed class MIGraphXTraceInfo
{
    internal MIGraphXTraceInfo(long index, string name, MIGraphXShape? resultShape)
    { Index = index; Name = name; ResultShape = resultShape; }

    /// <summary>主模块中的指令索引。 Instruction index in the main module.</summary>
    public long Index { get; }
    /// <summary>操作名称。 Operation name.</summary>
    public string Name { get; }
    /// <summary>结果形状；tuple、动态或未支持元素类型为 null。 Result shape; null for tuples, dynamic shapes or unsupported element types.</summary>
    public MIGraphXShape? ResultShape { get; }

    internal static MIGraphXTraceInfo Snapshot(IntPtr info)
    {
        var index = NativeValueOutput.ReadSizeT(output => NativeMethods.TraceInfoGetIndex(output, info), "migraphx_trace_info_get_index");
        var name = ReadPointer(output => NativeMethods.TraceInfoGetName(output, info), "migraphx_trace_info_get_name");
        var copiedName = StrictUtf8String.Decode(name, "migraphx_trace_info_get_name");
        var argument = ReadPointer(output => NativeMethods.TraceInfoGetResult(output, info), "migraphx_trace_info_get_result");
        NativeStatus.ThrowIfFailed(NativeMethods.ArgumentShape(out var shape, argument), "migraphx_argument_shape");
        shape = NativeBorrowedOutput.RequireHandle(shape, "migraphx_argument_shape");
        MIGraphXShape? copiedShape;
        try { copiedShape = MIGraphXShape.FromNative(shape, "trace result"); }
        catch (NotSupportedException) { copiedShape = null; }
        return new MIGraphXTraceInfo(NativeShapeSnapshot.ToLong(index, "trace index"), copiedName, copiedShape);
    }

    private static IntPtr ReadPointer(Func<IntPtr, NativeMIGraphXStatus> invoke, string operation)
    {
        var slot = System.Runtime.InteropServices.Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            System.Runtime.InteropServices.Marshal.WriteIntPtr(slot, IntPtr.Zero);
            NativeStatus.ThrowIfFailed(invoke(slot), operation);
            return NativeBorrowedOutput.RequireHandle(System.Runtime.InteropServices.Marshal.ReadIntPtr(slot), operation);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(slot); }
    }
}
