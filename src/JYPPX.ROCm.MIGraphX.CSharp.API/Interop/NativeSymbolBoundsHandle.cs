using System;
using System.Collections.Generic;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class NativeSymbolBoundsHandle : NativeOwnedHandle
{
    internal static NativeSymbolBoundsHandle Create(IReadOnlyDictionary<string, MIGraphXDynamicDimension> symbols)
    {
        var output = OutHandle<NativeSymbolBoundsHandle>.Create("migraphx_symbol_bounds_create");
        try
        {
            output.Complete(NativeMethods.SymbolBoundsCreate(output.OutSlot));
            foreach (var pair in symbols)
            {
                using var name = new StrictUtf8String(pair.Key, nameof(symbols));
                using var dimension = NativeDynamicDimensionHandle.Create(pair.Value);
                NativeStatus.ThrowIfFailed(NativeMethods.SymbolBoundsAdd(output.Handle.DangerousGetHandle(), name.Pointer, dimension.DangerousGetHandle()), "migraphx_symbol_bounds_add");
            }
            return output.Handle;
        }
        catch { output.Dispose(); throw; }
    }

    protected override bool ReleaseHandle() { NativeMethods.SymbolBoundsDestroy(handle); return true; }
}
