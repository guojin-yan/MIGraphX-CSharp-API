using System;
using Microsoft.Win32.SafeHandles;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal abstract class NativeOwnedHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected NativeOwnedHandle()
        : base(true)
    {
    }

    protected void Initialize(IntPtr raw) => SetHandle(raw);
}

internal sealed class NativeOnnxOptionsHandle : NativeOwnedHandle
{
    internal static NativeOnnxOptionsHandle Create()
    {
        var status = NativeMethods.OnnxOptionsCreate(out var raw);
        var owned = new NativeOnnxOptionsHandle();
        owned.Initialize(raw);
        return NativeHandleFactory.CompleteCreate(owned, status, "migraphx_onnx_options_create");
    }

    protected override bool ReleaseHandle() { NativeMethods.OnnxOptionsDestroy(handle); return true; }
}

internal sealed class NativeCompileOptionsHandle : NativeOwnedHandle
{
    internal static NativeCompileOptionsHandle Create(bool offloadCopy)
    {
        var status = NativeMethods.CompileOptionsCreate(out var raw);
        var owned = new NativeCompileOptionsHandle();
        owned.Initialize(raw);
        owned = NativeHandleFactory.CompleteCreate(owned, status, "migraphx_compile_options_create");
        try
        {
            NativeStatus.ThrowIfFailed(NativeMethods.CompileOptionsSetOffloadCopy(owned.DangerousGetHandle(), offloadCopy ? (byte)1 : (byte)0), "migraphx_compile_options_set_offload_copy");
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() { NativeMethods.CompileOptionsDestroy(handle); return true; }
}

internal sealed class NativeProgramParameterShapesHandle : NativeOwnedHandle
{
    internal static NativeProgramParameterShapesHandle Create(NativeProgramHandle program)
    {
        var status = NativeMethods.ProgramGetParameterShapes(out var raw, program.DangerousGetHandle());
        var owned = new NativeProgramParameterShapesHandle();
        owned.Initialize(raw);
        return NativeHandleFactory.CompleteCreate(owned, status, "migraphx_program_get_parameter_shapes");
    }

    protected override bool ReleaseHandle() { NativeMethods.ProgramParameterShapesDestroy(handle); return true; }
}

internal sealed class NativeShapesHandle : NativeOwnedHandle
{
    internal static NativeShapesHandle Create(NativeProgramHandle program)
    {
        var status = NativeMethods.ProgramGetOutputShapes(out var raw, program.DangerousGetHandle());
        var owned = new NativeShapesHandle();
        owned.Initialize(raw);
        return NativeHandleFactory.CompleteCreate(owned, status, "migraphx_program_get_output_shapes");
    }

    protected override bool ReleaseHandle() { NativeMethods.ShapesDestroy(handle); return true; }
}

internal sealed class NativeArgumentHandle : NativeOwnedHandle
{
    internal static NativeArgumentHandle Create(IntPtr shape, IntPtr buffer)
    {
        var status = NativeMethods.ArgumentCreate(out var raw, shape, buffer);
        var owned = new NativeArgumentHandle();
        owned.Initialize(raw);
        return NativeHandleFactory.CompleteCreate(owned, status, "migraphx_argument_create");
    }

    protected override bool ReleaseHandle() { NativeMethods.ArgumentDestroy(handle); return true; }
}

internal sealed class NativeProgramParametersHandle : NativeOwnedHandle
{
    internal static NativeProgramParametersHandle Create()
    {
        var status = NativeMethods.ProgramParametersCreate(out var raw);
        var owned = new NativeProgramParametersHandle();
        owned.Initialize(raw);
        return NativeHandleFactory.CompleteCreate(owned, status, "migraphx_program_parameters_create");
    }

    protected override bool ReleaseHandle() { NativeMethods.ProgramParametersDestroy(handle); return true; }
}

internal sealed class NativeArgumentsHandle : NativeOwnedHandle
{
    internal static NativeArgumentsHandle Run(NativeProgramHandle program, NativeProgramParametersHandle parameters)
    {
        var status = NativeMethods.ProgramRun(out var raw, program.DangerousGetHandle(), parameters.DangerousGetHandle());
        var owned = new NativeArgumentsHandle();
        owned.Initialize(raw);
        return NativeHandleFactory.CompleteCreate(owned, status, "migraphx_program_run");
    }

    protected override bool ReleaseHandle() { NativeMethods.ArgumentsDestroy(handle); return true; }
}

internal static class NativeHandleFactory
{
    internal static T CompleteCreate<T>(T owned, NativeMIGraphXStatus status, string operation) where T : NativeOwnedHandle
    {
        try
        {
            NativeStatus.ThrowIfFailed(status, operation);
            if (owned.IsInvalid)
            {
                throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, $"{operation} (success with null handle)");
            }
            return owned;
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }
}
