using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>实验 custom-op 的原始回调桥接。 Experimental custom-op callback bridge.</summary>
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
    private readonly List<Delegate> callbacks = new List<Delegate>();
    private readonly IntPtr stateHandle;
    private ComputeCallback? compute;
    private ComputeShapeCallback? computeShape;
    private OutputAliasCallback? outputAlias;
    private RunsOnOffloadTargetCallback? runsOnOffloadTarget;

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
        SetCallback(callback, NativeMethods.ExperimentalCustomOpSetCompute, "migraphx_experimental_custom_op_set_compute", value => compute = value);
    }

    /// <summary>设置 shape 推导回调。 Sets the shape-inference callback.</summary>
    /// <param name="callback">可为空的 shape 回调。 Optional shape callback.</param>
    public void SetComputeShape(ComputeShapeCallback? callback)
    {
        SetCallback(callback, NativeMethods.ExperimentalCustomOpSetComputeShape, "migraphx_experimental_custom_op_set_compute_shape", value => computeShape = value);
    }

    /// <summary>设置 output alias 回调。 Sets the output-alias callback.</summary>
    /// <param name="callback">可为空的 alias 回调。 Optional alias callback.</param>
    public void SetOutputAlias(OutputAliasCallback? callback)
    {
        SetCallback(callback, NativeMethods.ExperimentalCustomOpSetOutputAlias, "migraphx_experimental_custom_op_set_output_alias", value => outputAlias = value);
    }

    /// <summary>设置是否运行在 offload target 的回调。 Sets the runs-on-offload-target callback.</summary>
    /// <param name="callback">可为空的 target 回调。 Optional target callback.</param>
    public void SetRunsOnOffloadTarget(RunsOnOffloadTargetCallback? callback)
    {
        SetCallback(callback, NativeMethods.ExperimentalCustomOpSetRunsOnOffloadTarget, "migraphx_experimental_custom_op_set_runs_on_offload_target", value => runsOnOffloadTarget = value);
    }

    /// <summary>将 custom-op 注册到 MIGraphX 全局 registry。 Registers the custom-op in the MIGraphX global registry.</summary>
    public void Register() => owner.WithHandle(handle => NativeStatus.ThrowIfFailed(NativeMethods.ExperimentalCustomOpRegister(handle), "migraphx_experimental_custom_op_register"));

    /// <summary>重新创建 native 对象并重放当前回调，生成独立 custom-op 副本。 Recreates the native object and replays current callbacks into an independent custom-op.</summary>
    public MIGraphXExperimentalCustomOp Clone()
    {
        return owner.WithHandle(_ =>
        {
            lock (callbacks)
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
    public void Dispose() => owner.Dispose();

    private void SetCallback<T>(T? callback, Func<IntPtr, IntPtr, NativeMIGraphXStatus> setter, string operation, Action<T?> remember) where T : Delegate
    {
        var pointer = callback is null ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(callback);
        owner.WithHandle(handle =>
        {
            NativeStatus.ThrowIfFailed(setter(handle, pointer), operation);
            lock (callbacks)
            {
                if (callback is not null) callbacks.Add(callback);
                remember(callback);
            }
        });
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeMIGraphXStatus NativeExperimentalCopyCallback(IntPtr output, IntPtr input);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NativeMIGraphXStatus NativeExperimentalDeleteCallback(IntPtr input);

    private static NativeMIGraphXStatus CopyState(IntPtr output, IntPtr input)
    {
        try
        {
            if (output == IntPtr.Zero || input == IntPtr.Zero) return NativeMIGraphXStatus.BadParameter;
            var source = GCHandle.FromIntPtr(input);
            var clone = GCHandle.Alloc(source.Target, GCHandleType.Normal);
            Marshal.WriteIntPtr(output, GCHandle.ToIntPtr(clone));
            return NativeMIGraphXStatus.Success;
        }
        catch { return NativeMIGraphXStatus.UnknownError; }
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
