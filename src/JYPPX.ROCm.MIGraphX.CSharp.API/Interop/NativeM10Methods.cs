using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class NativeM10Methods
{
    internal const string RegistrySizeEntryPoint = "migraphx_get_onnx_operators_size";
    internal const string RegistryNameEntryPoint = "migraphx_get_onnx_operator_name_at_index";
    internal const string ArgumentEqualEntryPoint = "migraphx_argument_equal";
    internal const string ProgramEqualEntryPoint = "migraphx_program_equal";

    internal static readonly string[] RegistryRequiredExports =
    {
        RegistrySizeEntryPoint,
        RegistryNameEntryPoint,
    };

    internal static readonly string[] EqualityRequiredExports =
    {
        ArgumentEqualEntryPoint,
        ProgramEqualEntryPoint,
    };

    internal static int GetOnnxOperatorCount()
    {
        var slot = Marshal.AllocHGlobal(UIntPtr.Size);
        try
        {
            WriteZero(slot, UIntPtr.Size);
            NativeStatus.ThrowIfFailed(NativeMethods.GetOnnxOperatorsSize(slot), RegistrySizeEntryPoint);
            var value = ReadUIntPtr(slot);
            if ((UIntPtr.Size == 8 && value.ToUInt64() > int.MaxValue)
                || (UIntPtr.Size == 4 && value.ToUInt32() > int.MaxValue))
            {
                throw new OverflowException($"{RegistrySizeEntryPoint} returned a count that exceeds the managed collection limit.");
            }
            return checked((int)(UIntPtr.Size == 8 ? value.ToUInt64() : value.ToUInt32()));
        }
        finally
        {
            Marshal.FreeHGlobal(slot);
        }
    }

    internal static string GetOnnxOperatorName(int index)
    {
        var operation = $"{RegistryNameEntryPoint} (index {index})";
        var slot = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(slot, IntPtr.Zero);
            NativeStatus.ThrowIfFailed(
                NativeMethods.GetOnnxOperatorNameAtIndex(slot, ToUIntPtr(index)),
                operation);
            try
            {
                return StrictUtf8String.Decode(Marshal.ReadIntPtr(slot), operation);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException($"{operation} returned invalid UTF-8.", exception);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(slot);
        }
    }

    internal static bool ArgumentContentEquals(IntPtr left, IntPtr right) => ReadBoolean(
        output => NativeMethods.ArgumentEqual(output, left, right),
        ArgumentEqualEntryPoint);

    internal static bool ProgramContentEquals(IntPtr left, IntPtr right) => ReadBoolean(
        output => NativeMethods.ProgramEqual(output, left, right),
        ProgramEqualEntryPoint);

    private static bool ReadBoolean(Func<IntPtr, NativeMIGraphXStatus> invoke, string operation)
    {
        var slot = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(slot, byte.MaxValue);
            NativeStatus.ThrowIfFailed(invoke(slot), operation);
            var value = Marshal.ReadByte(slot);
            if (value > 1)
            {
                throw new MIGraphXException(
                    (int)NativeMIGraphXStatus.UnknownError,
                    $"{operation} (success with invalid C bool {value})");
            }
            return value == 1;
        }
        finally
        {
            Marshal.FreeHGlobal(slot);
        }
    }

    private static UIntPtr ReadUIntPtr(IntPtr pointer) => UIntPtr.Size == 8
        ? new UIntPtr(unchecked((ulong)Marshal.ReadInt64(pointer)))
        : new UIntPtr(unchecked((uint)Marshal.ReadInt32(pointer)));

    private static UIntPtr ToUIntPtr(int value) => UIntPtr.Size == 8
        ? new UIntPtr(checked((ulong)value))
        : new UIntPtr(checked((uint)value));

    private static void WriteZero(IntPtr pointer, int byteCount)
    {
        for (var index = 0; index < byteCount; index++)
        {
            Marshal.WriteByte(pointer, index, 0);
        }
    }
}
