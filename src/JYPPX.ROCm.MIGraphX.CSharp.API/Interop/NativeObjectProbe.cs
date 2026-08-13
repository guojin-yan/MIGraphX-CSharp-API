namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeObjectProbe
{
    internal static void Execute(string targetName)
    {
        using (var sourceTarget = NativeTargetHandle.Create(targetName))
        using (var destinationTarget = NativeTargetHandle.Create(targetName))
        using (var sourceProgram = NativeProgramHandle.Create())
        using (var destinationProgram = NativeProgramHandle.Create())
        {
            NativeStatus.ThrowIfFailed(
                NativeMethods.TargetAssignTo(destinationTarget.DangerousGetHandle(), sourceTarget.DangerousGetHandle()),
                "migraphx_target_assign_to");
            NativeStatus.ThrowIfFailed(
                NativeMethods.ProgramAssignTo(destinationProgram.DangerousGetHandle(), sourceProgram.DangerousGetHandle()),
                "migraphx_program_assign_to");
        }
    }
}
