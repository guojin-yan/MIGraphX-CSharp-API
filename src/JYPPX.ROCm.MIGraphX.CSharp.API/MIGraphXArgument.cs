using System;
using System.IO;
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
    private readonly bool ownsBuffer;
    private IntPtr buffer;
    private int asyncLeaseCount;
    private bool disposeRequested;

    private MIGraphXArgument(NativeRuntime runtime, MIGraphXShape shape, byte[] bytes)
    {
        Shape = shape;
        ownsBuffer = true;
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

    private MIGraphXArgument(NativeRuntime runtime, MIGraphXShape shape, IntPtr externalBuffer)
    {
        if (externalBuffer == IntPtr.Zero) { throw new ArgumentException("The external buffer pointer must not be null.", nameof(externalBuffer)); }
        Shape = shape;
        ownsBuffer = false;
        buffer = externalBuffer;
        using (var nativeShape = NativeShapeHandle.Create(shape))
        {
            owner = new NativeResourceOwner<NativeArgumentHandle>(runtime, NativeArgumentHandle.Create(nativeShape.DangerousGetHandle(), buffer));
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

    /// <summary>创建由 native 分配并立即复制为 host-backed 的空 argument。 Creates an empty native allocation and copies it into a host-backed argument.</summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="shape">静态、standard、packed shape。 Static standard packed shape.</param>
    public static MIGraphXArgument CreateEmpty(string nativeLibraryPath, MIGraphXShape shape)
    {
        if (shape is null) { throw new ArgumentNullException(nameof(shape)); }
        ValidateDetachedShape(shape);
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        using (var nativeShape = NativeShapeHandle.Create(shape))
        using (var nativeArgument = NativeArgumentHandle.CreateEmpty(nativeShape.DangerousGetHandle()))
        {
            return CopyFromNative(runtime, nativeArgument.DangerousGetHandle(), "migraphx_argument_create_empty");
        }
    }

    /// <summary>使用固定 seed 生成随机值并复制为 host-backed argument。 Generates deterministic random values and copies them into a host-backed argument.</summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the MIGraphX C native library.</param>
    /// <param name="shape">静态、standard、packed shape。 Static standard packed shape.</param>
    /// <param name="seed">传给 native <c>size_t</c> 的随机种子。 Random seed passed as native <c>size_t</c>.</param>
    public static MIGraphXArgument Generate(string nativeLibraryPath, MIGraphXShape shape, ulong seed)
    {
        if (shape is null) { throw new ArgumentNullException(nameof(shape)); }
        ValidateDetachedShape(shape);
        if (UIntPtr.Size == 4 && seed > uint.MaxValue)
        {
            throw new OverflowException("The seed does not fit the current native size_t range.");
        }
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        using (var nativeShape = NativeShapeHandle.Create(shape))
        using (var nativeArgument = NativeArgumentHandle.Generate(nativeShape.DangerousGetHandle(), UIntPtr.Size == 4 ? new UIntPtr((uint)seed) : new UIntPtr(seed)))
        {
            return CopyFromNative(runtime, nativeArgument.DangerousGetHandle(), "migraphx_argument_generate");
        }
    }

    /// <summary>从绝对 MIGraphX argument 文件载入并复制为 host-backed argument。 Loads an argument file and copies it into a host-backed argument.</summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute path to the native library.</param>
    /// <param name="path">绝对 argument 文件路径。 Absolute argument-file path.</param>
    public static MIGraphXArgument Load(string nativeLibraryPath, string path)
    {
        var fullPath = ValidateInputPath(path, nameof(path));
        var runtime = NativeRuntime.Load(nativeLibraryPath);
        using (var utf8 = new StrictUtf8String(fullPath, nameof(path)))
        using (var nativeArgument = NativeArgumentHandle.Load(utf8.Pointer))
        {
            return CopyFromNative(runtime, nativeArgument.DangerousGetHandle(), "migraphx_argument_load");
        }
    }

    /// <summary>将 argument 保存到绝对路径。 Saves this argument to an absolute path.</summary>
    /// <param name="path">绝对输出路径。 Absolute output path.</param>
    public void Save(string path)
    {
        var fullPath = ValidateOutputPath(path, nameof(path));
        using (var utf8 = new StrictUtf8String(fullPath, nameof(path)))
        {
            owner.WithHandle(handle => NativeStatus.ThrowIfFailed(
                NativeMethods.ArgumentSave(handle, utf8.Pointer),
                "migraphx_argument_save"));
        }
    }

    /// <summary>复制为具有独立 host buffer 的新 argument；borrowed device argument 不支持此操作。 Creates a new argument with an independent host buffer; borrowed device arguments are not supported.</summary>
    public MIGraphXArgument Clone()
    {
        return owner.WithHandle(_ =>
        {
            if (!ownsBuffer)
            {
                throw new NotSupportedException("A borrowed device argument cannot be cloned into an independent host buffer.");
            }
            return new MIGraphXArgument(owner.Runtime, Shape, CopyBytesUnderLock());
        });
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
        if (!ownsBuffer) { throw new NotSupportedException("A borrowed device argument is not directly host-readable."); }
        var count = NativeShapeSnapshot.ToInt(Shape.ElementCount, "argument element count");
        return owner.WithHandle(_ =>
        {
            var bytes = CopyBytesUnderLock();
            var result = new T[count];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        });
    }

    /// <summary>
    /// 使用固定原生 <c>raw_data::operator==</c> 比较 shape 与逻辑 tensor 内容，不改变任一对象的所有权。
    /// Compares shape and logical tensor content through the fixed native <c>raw_data::operator==</c> without changing ownership of either object.
    /// </summary>
    /// <param name="other">由同一已加载 MIGraphX 原生库创建的另一个 host-backed argument。 Another host-backed argument created by the same loaded MIGraphX native library.</param>
    /// <returns>原生比较报告 shape 与逻辑 tensor 内容相同则为 <see langword="true"/>。 <see langword="true"/> when native comparison reports equal shape and logical tensor content.</returns>
    /// <remarks>
    /// ROCm 7.2.1 对两个原生空值先直接判等；否则先比较完整 shape，再按 shape 的逻辑视图比较可计算元素，非可计算类型按字节视图比较。当前公开 argument 均为独立、非空 host 副本，不公开原生空值；内部 device-borrowed argument 不接受此比较。该方法是显式、版本绑定的原生内容比较，不实现 <see cref="object.Equals(object)"/>、hash 或运算符语义，也不表示跨 runtime、host/device 或数值容差下的 tensor 等价。反向并发比较按稳定资源顺序加锁，Dispose 会等待正在进行的比较。
    /// ROCm 7.2.1 first treats two native empty values as equal; otherwise it compares the complete shape, then computable elements through the shape's logical view, while non-computable types use a byte view. Current public arguments are independent, non-empty host copies and do not expose the native empty form; internally device-borrowed arguments are rejected. This is explicit, version-bound native content comparison and does not define <see cref="object.Equals(object)"/>, hashing, or operator semantics, or mean tensor equivalence across runtimes, host/device storage, or numeric tolerances. Reverse concurrent comparisons use a stable resource lock order, and Dispose waits for an in-progress comparison.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> 为 null。 <paramref name="other"/> is null.</exception>
    /// <exception cref="ArgumentException">对象不属于同一原生 library root。 The objects do not belong to the same native library root.</exception>
    /// <exception cref="NotSupportedException">任一 argument 借用 device buffer。 Either argument borrows a device buffer.</exception>
    /// <exception cref="ObjectDisposedException">任一对象已释放。 Either object has been disposed.</exception>
    /// <exception cref="MIGraphXNativeLoadException">原生 equality 导出不可用。 The native equality export is unavailable.</exception>
    /// <exception cref="MIGraphXException">原生比较失败或返回非法 C bool。 Native comparison fails or returns an invalid C bool.</exception>
    public bool HasSameNativeContent(MIGraphXArgument other)
    {
        if (other is null) { throw new ArgumentNullException(nameof(other)); }
        owner.Runtime.RequireSame(other.owner.Runtime, nameof(other));
        owner.Runtime.RequireM10Equality();
        return NativeResourceLock.With(
            new[]
            {
                NativeResourceLock.Target(owner.Id, owner.Sync),
                NativeResourceLock.Target(other.owner.Id, other.owner.Sync),
            },
            () =>
            {
                _ = owner.HandleUnderLock;
                _ = other.owner.HandleUnderLock;
                if (!ownsBuffer || !other.ownsBuffer)
                {
                    throw new NotSupportedException("Native content comparison is supported only for host-backed arguments.");
                }
                return NativeM10Methods.ArgumentContentEquals(owner.HandleUnderLock, other.owner.HandleUnderLock);
            });
    }

    internal NativeResourceOwner<NativeArgumentHandle> Owner => owner;

    internal MIGraphXArgument CloneForMap()
    {
        return owner.WithHandle(_ => ownsBuffer
            ? new MIGraphXArgument(owner.Runtime, Shape, CopyBytesUnderLock())
            : new MIGraphXArgument(owner.Runtime, Shape, buffer));
    }

    internal static MIGraphXArgument CreateExternal(NativeRuntime runtime, MIGraphXShape shape, IntPtr buffer)
        => new MIGraphXArgument(runtime, shape, buffer);

    internal IDisposable AcquireAsyncLease()
    {
        lock (owner.Sync)
        {
            _ = owner.HandleUnderLock;
            asyncLeaseCount++;
            try { return new ArgumentAsyncLease(this, owner.AcquireLease()); }
            catch { asyncLeaseCount--; throw; }
        }
    }

    internal static MIGraphXArgument CopyFromNative(NativeRuntime runtime, IntPtr argument, string context)
        => CopyFromNative(runtime, argument, context, null);

    internal static MIGraphXArgument CopyFromNative(
        NativeRuntime runtime,
        IntPtr argument,
        string context,
        Func<IntPtr, int, byte[]>? copyBuffer)
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
        byte[] bytes;
        if (copyBuffer is not null)
        {
            bytes = copyBuffer(nativeBuffer, byteCount);
            if (bytes is null || bytes.Length != byteCount)
            {
                throw new InvalidOperationException("The device-to-host copier returned an invalid byte count.");
            }
        }
        else
        {
            bytes = new byte[byteCount];
            if (byteCount != 0) Marshal.Copy(nativeBuffer, bytes, 0, byteCount);
        }
        return new MIGraphXArgument(runtime, shape, bytes);
    }

    /// <summary>确定性释放 native argument 后再释放 owned host buffer；重复调用安全。 Deterministically releases the native argument before its owned host buffer; repeated calls are safe.</summary>
    public void Dispose()
    {
        lock (owner.Sync)
        {
            disposeRequested = true;
            owner.Dispose();
            TryFreeBufferUnderLock();
        }
    }

    private void ReleaseAsyncLease(NativeHandleLease handleLease)
    {
        handleLease.Dispose();
        lock (owner.Sync)
        {
            asyncLeaseCount--;
            TryFreeBufferUnderLock();
        }
    }

    private void TryFreeBufferUnderLock()
    {
        if (!disposeRequested || asyncLeaseCount != 0 || !ownsBuffer) return;
        var allocated = buffer;
        buffer = IntPtr.Zero;
        if (allocated != IntPtr.Zero) Marshal.FreeHGlobal(allocated);
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

    private static void ValidateDetachedShape(MIGraphXShape shape)
    {
        if (shape.IsDynamic || !shape.IsStandard || !shape.IsPacked)
        {
            throw new NotSupportedException("Public detached arguments require static standard packed shapes.");
        }
    }

    private static string ValidateInputPath(string path, string parameterName)
    {
        if (path is null) { throw new ArgumentNullException(parameterName); }
        if (!Path.IsPathRooted(path)) { throw new ArgumentException("The path must be absolute.", parameterName); }
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) { throw new FileNotFoundException("The argument file does not exist.", fullPath); }
        return fullPath;
    }

    private static string ValidateOutputPath(string path, string parameterName)
    {
        if (path is null) { throw new ArgumentNullException(parameterName); }
        if (!Path.IsPathRooted(path)) { throw new ArgumentException("The path must be absolute.", parameterName); }
        return Path.GetFullPath(path);
    }

    private sealed class ArgumentAsyncLease : IDisposable
    {
        private MIGraphXArgument? argument;
        private NativeHandleLease? handleLease;

        internal ArgumentAsyncLease(MIGraphXArgument argument, NativeHandleLease handleLease)
        {
            this.argument = argument;
            this.handleLease = handleLease;
        }

        public void Dispose()
        {
            var ownedArgument = System.Threading.Interlocked.Exchange(ref argument, null);
            var ownedHandle = System.Threading.Interlocked.Exchange(ref handleLease, null);
            if (ownedArgument is not null && ownedHandle is not null) ownedArgument.ReleaseAsyncLease(ownedHandle);
        }
    }
}
