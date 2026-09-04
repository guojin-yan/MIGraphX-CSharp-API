using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal sealed class MIGraphXNativeAsyncRun : IDisposable
{
    private readonly object sync = new object();
    private readonly NativeRuntime runtime;
    private NativeArgumentsHandle? nativeOutputs;
    private IDisposable? inputLeases;
    private MIGraphXArgumentCollection? outputs;
    private ExceptionDispatchInfo? completionError;
    private int completionState;
    private bool disposed;

    internal MIGraphXNativeAsyncRun(NativeRuntime runtime, NativeArgumentsHandle nativeOutputs, IDisposable inputLeases)
    {
        this.runtime = runtime;
        this.nativeOutputs = nativeOutputs;
        this.inputLeases = inputLeases;
    }

    ~MIGraphXNativeAsyncRun()
    {
        if (!IsCompleted) return;
        try { DisposeCompletedState(); }
        catch { }
    }

    internal bool IsCompleted => Volatile.Read(ref completionState) == 2;

    internal MIGraphXArgumentCollection Outputs
    {
        get
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(MIGraphXNativeAsyncRun));
                if (!IsCompleted) throw new InvalidOperationException("The asynchronous run has not completed.");
                completionError?.Throw();
                return outputs!;
            }
        }
    }

    internal void Complete(Func<IntPtr, int, byte[]>? copyDeviceBuffer = null)
    {
        if (Interlocked.CompareExchange(ref completionState, 1, 0) != 0) return;

        var copied = new List<MIGraphXArgument>();
        try
        {
            var native = nativeOutputs ?? throw new ObjectDisposedException(nameof(MIGraphXNativeAsyncRun));
            var count = ReadSize(native.DangerousGetHandle());
            for (var index = 0; index < count; index++)
            {
                NativeStatus.ThrowIfFailed(
                    NativeMethods.ArgumentsGet(out var argument, native.DangerousGetHandle(), new UIntPtr((uint)index)),
                    "migraphx_arguments_get");
                argument = NativeBorrowedOutput.RequireHandle(argument, "migraphx_arguments_get");
                copied.Add(MIGraphXArgument.CopyFromNative(runtime, argument, $"async output {index}", copyDeviceBuffer));
            }
            EnsureStableSize(native.DangerousGetHandle(), count);
            lock (sync) outputs = new MIGraphXArgumentCollection(copied);
        }
        catch (Exception error)
        {
            foreach (var argument in copied) argument.Dispose();
            lock (sync) completionError = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            nativeOutputs?.Dispose();
            nativeOutputs = null;
            inputLeases?.Dispose();
            inputLeases = null;
            Volatile.Write(ref completionState, 2);
        }
    }

    internal void ThrowIfFailed()
    {
        lock (sync)
        {
            if (!IsCompleted) throw new InvalidOperationException("The asynchronous run has not completed.");
            completionError?.Throw();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            if (!IsCompleted) throw new InvalidOperationException("The native asynchronous run must complete before disposal.");
            DisposeCompletedState();
        }
        GC.SuppressFinalize(this);
    }

    private void DisposeCompletedState()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            outputs?.Dispose();
            outputs = null;
        }
    }

    private static int ReadSize(IntPtr arguments)
    {
        var size = NativeValueOutput.ReadSizeT(
            output => NativeMethods.ArgumentsSizeRaw(output, arguments),
            "migraphx_arguments_size");
        return NativeShapeSnapshot.ToInt(size, "async output count");
    }

    private static int ReadStableSize(IntPtr arguments)
    {
        var first = ReadSize(arguments);
        var second = ReadSize(arguments);
        if (second != first)
        {
            throw new InvalidOperationException($"Native async output count changed from {first} to {second} while creating a snapshot.");
        }
        return second;
    }

    private static void EnsureStableSize(IntPtr arguments, int expected)
    {
        var first = ReadSize(arguments);
        var second = ReadSize(arguments);
        if (second != first)
        {
            throw new InvalidOperationException($"Native async output count changed from {first} to {second} while creating a snapshot.");
        }
        if (first != expected)
        {
            throw new InvalidOperationException($"Native async output count changed from {expected} to {first} while creating a snapshot.");
        }
    }
}
