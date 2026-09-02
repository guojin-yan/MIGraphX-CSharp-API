using System;
using System.Runtime.InteropServices;
using System.Text;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>实验 custom-op 的原始回调桥接。 Experimental custom-op callback bridge.</summary>
/// <remarks>
/// Managed callback exceptions are contained by a native ABI thunk. An exception is
/// converted to <see cref="MIGraphXStatus.UnknownError"/> and, when the native side
/// supplies a buffer, a bounded UTF-8 message is written to that buffer; no managed
/// exception is allowed to cross the unmanaged callback boundary.
/// 托管回调异常由原生 ABI thunk 截获并转换为 <see cref="MIGraphXStatus.UnknownError"/>，
/// 原生侧提供缓冲区时写入有界 UTF-8 消息；任何托管异常都不会穿过非托管回调边界。
/// </remarks>
public sealed class MIGraphXExperimentalCustomOp : IDisposable
{
    /// <summary>Compute 回调的原生 ABI 签名。 Native ABI signature for compute callbacks.</summary>
    /// <param name="outputArgument">输出 argument 指针。 Native output-argument pointer.</param>
    /// <param name="obj">custom-op 状态指针。 Custom-op state pointer.</param>
    /// <param name="exceptionMessage">异常消息缓冲区。 Exception-message buffer.</param>
    /// <param name="exceptionMessageSize">异常消息缓冲区大小。 Exception-message buffer size.</param>
    /// <param name="context">原生 context 指针。 Native context pointer.</param>
    /// <param name="outputShape">输出 shape 指针。 Output-shape pointer.</param>
    /// <param name="inputs">输入集合指针。 Input collection pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate MIGraphXStatus ComputeCallback(IntPtr outputArgument, IntPtr obj, IntPtr exceptionMessage, UIntPtr exceptionMessageSize, IntPtr context, IntPtr outputShape, IntPtr inputs);
    /// <summary>Shape 推导回调的原生 ABI 签名。 Native ABI signature for shape-inference callbacks.</summary>
    /// <param name="outputShape">输出 shape 指针。 Output-shape pointer.</param>
    /// <param name="obj">custom-op 状态指针。 Custom-op state pointer.</param>
    /// <param name="exceptionMessage">异常消息缓冲区。 Exception-message buffer.</param>
    /// <param name="exceptionMessageSize">异常消息缓冲区大小。 Exception-message buffer size.</param>
    /// <param name="inputs">输入集合指针。 Input collection pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate MIGraphXStatus ComputeShapeCallback(IntPtr outputShape, IntPtr obj, IntPtr exceptionMessage, UIntPtr exceptionMessageSize, IntPtr inputs);
    /// <summary>Output alias 回调的原生 ABI 签名。 Native ABI signature for output-alias callbacks.</summary>
    /// <param name="output">输出别名索引指针。 Output-alias index pointer.</param>
    /// <param name="outputSize">输出别名大小指针。 Output-alias size pointer.</param>
    /// <param name="obj">custom-op 状态指针。 Custom-op state pointer.</param>
    /// <param name="exceptionMessage">异常消息缓冲区。 Exception-message buffer.</param>
    /// <param name="exceptionMessageSize">异常消息缓冲区大小。 Exception-message buffer size.</param>
    /// <param name="inputs">输入集合指针。 Input collection pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate MIGraphXStatus OutputAliasCallback(IntPtr output, IntPtr outputSize, IntPtr obj, IntPtr exceptionMessage, UIntPtr exceptionMessageSize, IntPtr inputs);
    /// <summary>Offload-target 回调的原生 ABI 签名。 Native ABI signature for offload-target callbacks.</summary>
    /// <param name="output">布尔输出指针。 Boolean output pointer.</param>
    /// <param name="obj">custom-op 状态指针。 Custom-op state pointer.</param>
    /// <param name="exceptionMessage">异常消息缓冲区。 Exception-message buffer.</param>
    /// <param name="exceptionMessageSize">异常消息缓冲区大小。 Exception-message buffer size.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate MIGraphXStatus RunsOnOffloadTargetCallback(IntPtr output, IntPtr obj, IntPtr exceptionMessage, UIntPtr exceptionMessageSize);

    private static readonly NativeExperimentalCopyCallback CopyThunk = CopyState;
    private static readonly NativeExperimentalDeleteCallback DeleteThunk = DeleteState;
    private readonly NativeResourceOwner<NativeExperimentalCustomOpHandle> owner;
    private readonly object? state;
    private readonly object callbackSync = new object();
    private readonly IntPtr stateHandle;
    private ComputeCallback? compute;
    private ComputeShapeCallback? computeShape;
    private OutputAliasCallback? outputAlias;
    private RunsOnOffloadTargetCallback? runsOnOffloadTarget;
    private Delegate? computeThunk;
    private Delegate? computeShapeThunk;
    private Delegate? outputAliasThunk;
    private Delegate? runsOnOffloadTargetThunk;

    /// <summary>创建实验 custom-op。 Creates an experimental custom-op.</summary>
    /// <param name="nativeLibraryPath">MIGraphX C 原生库绝对路径。 Absolute MIGraphX C native-library path.</param>
    /// <param name="name">注册名称。 Registration name.</param>
    /// <param name="state">由 copy/delete 回调保有的托管状态。 Managed state retained by copy/delete callbacks.</param>
    /// <param name="objectTypeName">native 对象类型名称。 Native object type name.</param>
    public MIGraphXExperimentalCustomOp(string nativeLibraryPath, string name, object? state = null, string objectTypeName = "MIGraphXSharp.CustomOp")
        : this(NativeRuntime.Load(nativeLibraryPath), name, state, objectTypeName)
    {
    }

    private MIGraphXExperimentalCustomOp(NativeRuntime runtime, string name, object? state, string objectTypeName)
    {
        if (name is null) { throw new ArgumentNullException(nameof(name)); }
        if (objectTypeName is null) { throw new ArgumentNullException(nameof(objectTypeName)); }
        this.state = state;
        using (var typeName = new StrictUtf8String(objectTypeName, nameof(objectTypeName)))
        using (var utf8Name = new StrictUtf8String(name, nameof(name)))
        {
            stateHandle = GCHandle.ToIntPtr(GCHandle.Alloc(state ?? new object(), GCHandleType.Normal));
            owner = new NativeResourceOwner<NativeExperimentalCustomOpHandle>(runtime, NativeExperimentalCustomOpHandle.Create(
                stateHandle,
                Marshal.GetFunctionPointerForDelegate(CopyThunk),
                Marshal.GetFunctionPointerForDelegate(DeleteThunk),
                typeName.Pointer,
                utf8Name.Pointer,
                () => GCHandle.FromIntPtr(stateHandle).Free()));
        }
        Name = name;
        ObjectTypeName = objectTypeName;
    }

    /// <summary>获取注册名称。 Gets the registration name.</summary>
    public string Name { get; }
    /// <summary>获取 native object type name。 Gets the native object type name.</summary>
    public string ObjectTypeName { get; }
    /// <summary>获取托管状态。 Gets the managed state.</summary>
    public object? State => state;

    /// <summary>设置 compute 回调；回调参数保持原生 ABI 的不透明指针。 Sets the compute callback with opaque native ABI pointers.</summary>
    /// <param name="callback">可为空的 compute 回调。 Optional compute callback.</param>
    public void SetCompute(ComputeCallback? callback)
    {
        NativeExperimentalCustomOpComputeCallback? thunk = callback is null ? null :
            (outputArgument, obj, exceptionMessage, exceptionMessageSize, context, outputShape, inputs) =>
                InvokeCompute(callback, outputArgument, obj, exceptionMessage, exceptionMessageSize, context, outputShape, inputs);
        SetCallback(thunk, NativeMethods.ExperimentalCustomOpSetCompute, "migraphx_experimental_custom_op_set_compute", value => computeThunk = value, () =>
        {
            compute = callback;
        });
    }

    /// <summary>设置 shape 推导回调。 Sets the shape-inference callback.</summary>
    /// <param name="callback">可为空的 shape 回调。 Optional shape callback.</param>
    public void SetComputeShape(ComputeShapeCallback? callback)
    {
        NativeExperimentalCustomOpComputeShapeCallback? thunk = callback is null ? null :
            (outputShape, obj, exceptionMessage, exceptionMessageSize, inputs) =>
                InvokeComputeShape(callback, outputShape, obj, exceptionMessage, exceptionMessageSize, inputs);
        SetCallback(thunk, NativeMethods.ExperimentalCustomOpSetComputeShape, "migraphx_experimental_custom_op_set_compute_shape", value => computeShapeThunk = value, () =>
        {
            computeShape = callback;
        });
    }

    /// <summary>设置 output alias 回调。 Sets the output-alias callback.</summary>
    /// <param name="callback">可为空的 alias 回调。 Optional alias callback.</param>
    public void SetOutputAlias(OutputAliasCallback? callback)
    {
        NativeExperimentalCustomOpOutputAliasCallback? thunk = callback is null ? null :
            (output, outputSize, obj, exceptionMessage, exceptionMessageSize, inputs) =>
                InvokeOutputAlias(callback, output, outputSize, obj, exceptionMessage, exceptionMessageSize, inputs);
        SetCallback(thunk, NativeMethods.ExperimentalCustomOpSetOutputAlias, "migraphx_experimental_custom_op_set_output_alias", value => outputAliasThunk = value, () =>
        {
            outputAlias = callback;
        });
    }

    /// <summary>设置是否运行在 offload target 的回调。 Sets the runs-on-offload-target callback.</summary>
    /// <param name="callback">可为空的 target 回调。 Optional target callback.</param>
    public void SetRunsOnOffloadTarget(RunsOnOffloadTargetCallback? callback)
    {
        NativeExperimentalCustomOpRunsOnOffloadTargetCallback? thunk = callback is null ? null :
            (output, obj, exceptionMessage, exceptionMessageSize) =>
                InvokeRunsOnOffloadTarget(callback, output, obj, exceptionMessage, exceptionMessageSize);
        SetCallback(thunk, NativeMethods.ExperimentalCustomOpSetRunsOnOffloadTarget, "migraphx_experimental_custom_op_set_runs_on_offload_target", value => runsOnOffloadTargetThunk = value, () =>
        {
            runsOnOffloadTarget = callback;
        });
    }

    /// <summary>将 custom-op 注册到 MIGraphX 全局 registry。 Registers the custom-op in the MIGraphX global registry.</summary>
    public void Register() => owner.WithHandle(handle => NativeStatus.ThrowIfFailed(NativeMethods.ExperimentalCustomOpRegister(handle), "migraphx_experimental_custom_op_register"));

    /// <summary>重新创建 native 对象并重放当前回调，生成独立 custom-op 副本。 Recreates the native object and replays current callbacks into an independent custom-op.</summary>
    public MIGraphXExperimentalCustomOp Clone()
    {
        return owner.WithHandle(_ =>
        {
            lock (callbackSync)
            {
                var result = new MIGraphXExperimentalCustomOp(owner.Runtime, Name, state, ObjectTypeName);
                try
                {
                    if (compute is not null) result.SetCompute(compute);
                    if (computeShape is not null) result.SetComputeShape(computeShape);
                    if (outputAlias is not null) result.SetOutputAlias(outputAlias);
                    if (runsOnOffloadTarget is not null) result.SetRunsOnOffloadTarget(runsOnOffloadTarget);
                    return result;
                }
                catch
                {
                    result.Dispose();
                    throw;
                }
            }
        });
    }

    internal NativeResourceOwner<NativeExperimentalCustomOpHandle> Owner => owner;
    /// <summary>释放 native custom-op。 Releases the native custom-op.</summary>
    public void Dispose()
    {
        // Keep callback thunks rooted until native destroy has finished consuming the owner.
        owner.Dispose();
        lock (callbackSync)
        {
            computeThunk = null;
            computeShapeThunk = null;
            outputAliasThunk = null;
            runsOnOffloadTargetThunk = null;
            compute = null;
            computeShape = null;
            outputAlias = null;
            runsOnOffloadTarget = null;
        }
    }

    private void SetCallback(Delegate? callback, Func<IntPtr, IntPtr, NativeMIGraphXStatus> setter, string operation, Action<Delegate?> retain, Action remember)
    {
        var pointer = callback is null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(callback);
        owner.WithHandle(handle =>
        {
            NativeMIGraphXStatus status;
            try
            {
                status = setter(handle, pointer);
            }
            finally
            {
                // Keep the thunk alive until the native setter has finished consuming its function pointer.
                GC.KeepAlive(callback);
            }
            NativeStatus.ThrowIfFailed(status, operation);
            lock (callbackSync)
            {
                retain(callback);
                remember();
            }
        });
    }

    private static NativeMIGraphXStatus InvokeCompute(
        ComputeCallback callback,
        IntPtr outputArgument,
        IntPtr obj,
        IntPtr exceptionMessage,
        UIntPtr exceptionMessageSize,
        IntPtr context,
        IntPtr outputShape,
        IntPtr inputs)
    {
        try
        {
            return (NativeMIGraphXStatus)callback(outputArgument, obj, exceptionMessage, exceptionMessageSize, context, outputShape, inputs);
        }
        catch (Exception error)
        {
            WriteCallbackException(exceptionMessage, exceptionMessageSize, error);
            return NativeMIGraphXStatus.UnknownError;
        }
    }

    private static NativeMIGraphXStatus InvokeComputeShape(
        ComputeShapeCallback callback,
        IntPtr outputShape,
        IntPtr obj,
        IntPtr exceptionMessage,
        UIntPtr exceptionMessageSize,
        IntPtr inputs)
    {
        try
        {
            return (NativeMIGraphXStatus)callback(outputShape, obj, exceptionMessage, exceptionMessageSize, inputs);
        }
        catch (Exception error)
        {
            WriteCallbackException(exceptionMessage, exceptionMessageSize, error);
            return NativeMIGraphXStatus.UnknownError;
        }
    }

    private static NativeMIGraphXStatus InvokeOutputAlias(
        OutputAliasCallback callback,
        IntPtr output,
        IntPtr outputSize,
        IntPtr obj,
        IntPtr exceptionMessage,
        UIntPtr exceptionMessageSize,
        IntPtr inputs)
    {
        try
        {
            return (NativeMIGraphXStatus)callback(output, outputSize, obj, exceptionMessage, exceptionMessageSize, inputs);
        }
        catch (Exception error)
        {
            WriteCallbackException(exceptionMessage, exceptionMessageSize, error);
            return NativeMIGraphXStatus.UnknownError;
        }
    }

    private static NativeMIGraphXStatus InvokeRunsOnOffloadTarget(
        RunsOnOffloadTargetCallback callback,
        IntPtr output,
        IntPtr obj,
        IntPtr exceptionMessage,
        UIntPtr exceptionMessageSize)
    {
        try
        {
            return (NativeMIGraphXStatus)callback(output, obj, exceptionMessage, exceptionMessageSize);
        }
        catch (Exception error)
        {
            WriteCallbackException(exceptionMessage, exceptionMessageSize, error);
            return NativeMIGraphXStatus.UnknownError;
        }
    }

    private static void WriteCallbackException(IntPtr exceptionMessage, UIntPtr exceptionMessageSize, Exception error)
    {
        try
        {
            if (exceptionMessage == IntPtr.Zero) return;
            var rawSize = exceptionMessageSize.ToUInt64();
            if (rawSize == 0) return;
            var capacity = rawSize > int.MaxValue ? int.MaxValue : (int)rawSize;
            if (capacity <= 0) return;
            var text = string.IsNullOrEmpty(error.Message) ? error.GetType().Name : error.Message;
            var bytes = Encoding.UTF8.GetBytes(text);
            var copy = Math.Min(bytes.Length, capacity - 1);
            if (copy < bytes.Length)
            {
                while (copy > 0 && (bytes[copy] & 0xC0) == 0x80) copy--;
            }
            if (copy > 0) Marshal.Copy(bytes, 0, exceptionMessage, copy);
            Marshal.WriteByte(exceptionMessage, copy, 0);
        }
        catch
        {
            // A callback must never let a secondary exception cross the unmanaged boundary.
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeMIGraphXStatus NativeExperimentalCopyCallback(IntPtr output, IntPtr input);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeMIGraphXStatus NativeExperimentalDeleteCallback(IntPtr input);

    private static NativeMIGraphXStatus CopyState(IntPtr output, IntPtr input)
    {
        var clone = default(GCHandle);
        try
        {
            if (output == IntPtr.Zero || input == IntPtr.Zero) return NativeMIGraphXStatus.BadParameter;
            var source = GCHandle.FromIntPtr(input);
            clone = GCHandle.Alloc(source.Target, GCHandleType.Normal);
            Marshal.WriteIntPtr(output, GCHandle.ToIntPtr(clone));
            return NativeMIGraphXStatus.Success;
        }
        catch
        {
            try { if (clone.IsAllocated) clone.Free(); }
            catch { }
            return NativeMIGraphXStatus.UnknownError;
        }
    }

    private static NativeMIGraphXStatus DeleteState(IntPtr input)
    {
        try
        {
            if (input != IntPtr.Zero) GCHandle.FromIntPtr(input).Free();
            return NativeMIGraphXStatus.Success;
        }
        catch { return NativeMIGraphXStatus.UnknownError; }
    }
}
