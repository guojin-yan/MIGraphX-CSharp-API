using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using JYPPX.ROCm.HipSharp.Interop;
using JYPPX.ROCm.HipSharp.Memory;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.HipSharp.Types;
using JYPPX.ROCm.MIGraphXSharp;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;

/// <summary>提供 MIGraphXProgram 与 HipSharp stream/device memory 的受租约异步互操作。 Provides leased asynchronous interoperation between MIGraphXProgram and HipSharp streams/device memory.</summary>
public static class MIGraphXHipExecution
{
    /// <summary>在 HIP stream 上提交 host parameter map；program 必须以 offloadCopy=true 编译。 Enqueues a host parameter map on a HIP stream; the program must be compiled with offloadCopy=true.</summary>
    /// <param name="program">已编译 program。 Compiled program.</param>
    /// <param name="parameters">包含 owned host copies 的参数映射。 Parameter map containing owned host copies.</param>
    /// <param name="stream">非 capture 状态的 HipSharp stream。 Non-capturing HipSharp stream.</param>
    /// <returns>显式完成与清理对象。 Explicit completion and cleanup object.</returns>
    public static MIGraphXHipAsyncRun RunHostAsync(this MIGraphXProgram program, MIGraphXParameterMap parameters, HipStream stream)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var state = stream.EnqueuePending(
            handle => program.EnqueueNativeAsync(parameters, handle, requireOffloadCopy: true),
            result => result.Complete());
        return new MIGraphXHipAsyncRun(stream, state);
    }

    /// <summary>借用 HipDeviceMemory 输入并在 HIP stream 上提交；program 必须以 offloadCopy=false 编译，完成时显式 D2H 固化 outputs。 Borrows HipDeviceMemory inputs and enqueues on a HIP stream; the program must use offloadCopy=false and completion explicitly snapshots outputs through D2H copies.</summary>
    /// <param name="program">已编译 program。 Compiled program.</param>
    /// <param name="inputs">完整且唯一的具名设备输入集合。 Complete unique named device inputs.</param>
    /// <param name="stream">与所有输入属于同一 HIP runtime client 和 device 的 stream。 Stream sharing one HIP runtime client and device with every input.</param>
    /// <returns>完成后拥有 host output 副本的结果。 Result owning host-output copies after completion.</returns>
    public static MIGraphXHipAsyncRun RunDeviceAsync(this MIGraphXProgram program, IReadOnlyList<MIGraphXHipDeviceInput> inputs, HipStream stream)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var supplied = inputs.ToArray();
        if (supplied.Any(input => input is null)) throw new ArgumentException("Device inputs must not contain null values.", nameof(inputs));
        ValidateDeviceInputs(program, supplied, stream);

        var pointerLeases = new List<IDisposable>();
        using (var parameters = new MIGraphXParameterMap(program.Runtime))
        {
            try
            {
                foreach (var input in supplied)
                {
                    var pointer = input.Memory.DangerousAcquireHandle(out var addedReference);
                    var lease = new DevicePointerLease(input.Memory, addedReference);
                    pointerLeases.Add(lease);
                    parameters.AddExternal(input.Name, input.Shape, pointer);
                }

                var state = stream.EnqueuePending(
                    handle => program.EnqueueNativeAsync(parameters, handle, requireOffloadCopy: false, pointerLeases),
                    result => result.Complete((pointer, count) => CopyDeviceToHost(stream.NativeApi, pointer, count)));
                return new MIGraphXHipAsyncRun(stream, state);
            }
            catch
            {
                for (var index = pointerLeases.Count - 1; index >= 0; index--) pointerLeases[index].Dispose();
                throw;
            }
        }
    }

    private static void ValidateDeviceInputs(MIGraphXProgram program, MIGraphXHipDeviceInput[] inputs, HipStream stream)
    {
        var required = program.GetParameterShapes();
        if (inputs.Length != required.Count || inputs.Select(input => input.Name).Distinct(StringComparer.Ordinal).Count() != inputs.Length)
            throw new ArgumentException("Device input names must be unique and exactly match the model parameters.", nameof(inputs));

        foreach (var input in inputs)
        {
            if (!required.TryGetValue(input.Name, out var expected)) throw new ArgumentException($"Unknown model input '{input.Name}'.", nameof(inputs));
            if (input.Shape.IsDynamic || !input.Shape.IsStandard || !input.Shape.IsPacked)
                throw new ArgumentException($"Device input '{input.Name}' requires a concrete standard packed shape.", nameof(inputs));
            if (!SameShape(expected, input.Shape)) throw new ArgumentException($"Device input '{input.Name}' shape does not match the compiled model.", nameof(inputs));
            if (input.Shape.ByteCount < 0 || (ulong)input.Shape.ByteCount > input.Memory.ByteLength)
                throw new ArgumentException($"Device input '{input.Name}' exceeds its memory capacity.", nameof(inputs));
            if (!ReferenceEquals(input.Memory.NativeApi, stream.NativeApi))
                throw new ArgumentException($"Device input '{input.Name}' and stream belong to different HIP Runtime clients.", nameof(stream));
            if (input.Memory.DeviceOrdinal != stream.DeviceOrdinal)
                throw new ArgumentException($"Device input '{input.Name}' and stream belong to different devices.", nameof(stream));
        }
    }

    private static bool SameShape(MIGraphXShape left, MIGraphXShape right)
        => !left.IsDynamic && left.DataType == right.DataType && left.Lengths.SequenceEqual(right.Lengths);

    private static byte[] CopyDeviceToHost(IHipNativeApi nativeApi, IntPtr pointer, int count)
    {
        var bytes = new byte[count];
        if (count == 0) return bytes;
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            HipCall.ThrowIfFailed(nativeApi, nativeApi.Memcpy(
                pinned.AddrOfPinnedObject(), pointer, HipDeviceMemory.ToUIntPtr((ulong)count, nameof(count)), HipMemoryCopyKind.DeviceToHost),
                "hipMemcpy(device-to-host MIGraphX output)");
            return bytes;
        }
        finally { pinned.Free(); }
    }

    private sealed class DevicePointerLease : IDisposable
    {
        private HipDeviceMemory? memory;
        private readonly bool addedReference;
        internal DevicePointerLease(HipDeviceMemory memory, bool addedReference) { this.memory = memory; this.addedReference = addedReference; }
        public void Dispose()
        {
            var owned = System.Threading.Interlocked.Exchange(ref memory, null);
            if (owned is not null && addedReference) owned.DangerousReleaseHandle();
        }
    }
}
