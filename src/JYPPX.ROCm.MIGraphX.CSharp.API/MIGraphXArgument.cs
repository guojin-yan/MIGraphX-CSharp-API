using System;
using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示拥有 native argument handle 与其 host buffer 的资源安全 typed argument。
/// Represents a resource-safe typed argument that owns both its native argument handle and host buffer.
/// </summary>
public sealed class MIGraphXArgument : IDisposable
{
    private readonly NativeResourceOwner<NativeArgumentHandle> owner;
    private IntPtr buffer;

    private MIGraphXArgument(NativeRuntime runtime, MIGraphXShape shape, byte[] bytes)
    {
        Shape = shape;
        var allocationSize = Math.Max(1, bytes.Length);
        buffer = Marshal.AllocHGlobal(allocationSize);
        try
        {
            if (bytes.Length != 0)
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
            }

            using (var nativeShape = NativeShapeHandle.Create(shape))
            {
                owner = new NativeResourceOwner<NativeArgumentHandle>(runtime, NativeArgumentHandle.Create(nativeShape.DangerousGetHandle(), buffer));
            }
        }
        catch
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            throw;
        }
    }

    /// <summary>获取不依赖 borrowed native shape 的不可变元数据。 Gets immutable metadata independent of a borrowed native shape.</summary>
    public MIGraphXShape Shape { get; }

    /// <summary>
    /// 深拷贝 typed 数组并创建 host-backed native argument。
    /// Deep-copies a typed array and creates a host-backed native argument.
    /// </summary>
    /// <typeparam name="T">必须与 shape 元素类型精确映射的 unmanaged 标量。 An unmanaged scalar that must exactly map to the shape element type.</typeparam>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="shape">静态、standard、packed 的 shape 元数据。 Static, standard, packed shape metadata.</param>
    /// <param name="values">元素数必须与 shape 完全匹配的输入值。 Input values whose count must exactly match the shape.</param>
    /// <returns>拥有独立 host buffer 的 argument。 An argument owning an independent host buffer.</returns>
    public static MIGraphXArgument Create<T>(string nativeLibraryPath, MIGraphXShape shape, T[] values)
        where T : unmanaged
    {
        if (shape is null) { throw new ArgumentNullException(nameof(shape)); }
        if (values is null) { throw new ArgumentNullException(nameof(values)); }
        ValidateType<T>(shape);
        if (!shape.IsStandard || !shape.IsPacked) { throw new NotSupportedException("M4 typed host arguments require standard packed shapes."); }
        if (shape.ElementCount > int.MaxValue || shape.ByteCount > int.MaxValue)
        {
            throw new OverflowException("The shape exceeds the managed array or copy limit.");
        }
        if (values.LongLength != shape.ElementCount)
        {
            throw new ArgumentException($"Input element count {values.LongLength} does not match shape element count {shape.ElementCount}.", nameof(values));
        }

        var bytes = new byte[checked((int)shape.ByteCount)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return new MIGraphXArgument(NativeRuntime.Load(nativeLibraryPath), shape, bytes);
    }

    /// <summary>
    /// 将 owned host buffer 复制为新的 typed 数组。
    /// Copies the owned host buffer into a new typed array.
    /// </summary>
    /// <typeparam name="T">必须与 shape 元素类型精确映射的 unmanaged 标量。 An unmanaged scalar that must exactly map to the shape element type.</typeparam>
    /// <returns>与 argument 生命周期独立的数组。 An array independent of the argument lifetime.</returns>
    public T[] ToArray<T>() where T : unmanaged
    {
        ValidateType<T>(Shape);
        var count = NativeShapeSnapshot.ToInt(Shape.ElementCount, "argument element count");
        return owner.WithHandle(_ =>
        {
            var bytes = CopyBytesUnderLock();
            var result = new T[count];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        });
    }

    internal NativeResourceOwner<NativeArgumentHandle> Owner => owner;

    internal MIGraphXArgument CloneForMap()
    {
        return owner.WithHandle(_ => new MIGraphXArgument(owner.Runtime, Shape, CopyBytesUnderLock()));
    }

    internal static MIGraphXArgument CopyFromNative(NativeRuntime runtime, IntPtr argument, string context)
    {
        if (argument == IntPtr.Zero)
        {
            throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, $"{context} (null borrowed argument)");
        }
        NativeStatus.ThrowIfFailed(NativeMethods.ArgumentShape(out var shapeHandle, argument), "migraphx_argument_shape");
        var shape = MIGraphXShape.FromNative(shapeHandle, context);
        if (!shape.IsStandard || !shape.IsPacked)
        {
            throw new NotSupportedException("M4 detached output arguments require standard packed shapes.");
        }
        var byteCount = NativeShapeSnapshot.ToInt(shape.ByteCount, "argument byte count");
        NativeStatus.ThrowIfFailed(NativeMethods.ArgumentBuffer(out var nativeBuffer, argument), "migraphx_argument_buffer");
        if (byteCount != 0 && nativeBuffer == IntPtr.Zero)
        {
            throw new MIGraphXException((int)NativeMIGraphXStatus.UnknownError, "migraphx_argument_buffer (success with null buffer)");
        }
        var bytes = new byte[byteCount];
        if (byteCount != 0)
        {
            Marshal.Copy(nativeBuffer, bytes, 0, byteCount);
        }
        return new MIGraphXArgument(runtime, shape, bytes);
    }

    /// <summary>确定性释放 native argument 后再释放 owned host buffer；重复调用安全。 Deterministically releases the native argument before its owned host buffer; repeated calls are safe.</summary>
    public void Dispose()
    {
        lock (owner.Sync)
        {
            owner.Dispose();
            var allocated = buffer;
            buffer = IntPtr.Zero;
            if (allocated != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(allocated);
            }
        }
    }

    private byte[] CopyBytesUnderLock()
    {
        var byteCount = NativeShapeSnapshot.ToInt(Shape.ByteCount, "argument byte count");
        var bytes = new byte[byteCount];
        if (byteCount != 0)
        {
            Marshal.Copy(buffer, bytes, 0, byteCount);
        }
        return bytes;
    }

    private static void ValidateType<T>(MIGraphXShape shape) where T : unmanaged
    {
        var mapped = ShapeDataTypeMap.FromManagedType(typeof(T));
        if (mapped != shape.DataType)
        {
            throw new ArgumentException($"Managed element type '{typeof(T).FullName}' maps to {mapped}, not shape type {shape.DataType}.", nameof(T));
        }
    }
}
