using System;
using System.Runtime.InteropServices;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>与 program 绑定的实验 context 视图。 Experimental context view tied to a program.</summary>
public sealed class MIGraphXContext : IDisposable
{
    private readonly object sync = new object();
    private NativeHandleLease? programLease;
    private readonly IntPtr handle;

    internal MIGraphXContext(NativeHandleLease programLease, IntPtr handle)
    {
        if (handle == IntPtr.Zero) throw new ArgumentException("The native context handle must not be null.", nameof(handle));
        this.programLease = programLease;
        this.handle = handle;
    }

    /// <summary>完成 context 上排队的工作。 Finishes work queued on the context.</summary>
    public void Finish()
    {
        lock (sync)
        {
            _ = programLease ?? throw new ObjectDisposedException(nameof(MIGraphXContext));
            NativeStatus.ThrowIfFailed(NativeMethods.ContextFinish(handle), "migraphx_context_finish");
        }
    }

    /// <summary>获取底层 HIP queue 指针；调用方不取得其所有权。 Gets the underlying HIP queue pointer without transferring ownership.</summary>
    public IntPtr Queue
    {
        get
        {
            lock (sync)
            {
                _ = programLease ?? throw new ObjectDisposedException(nameof(MIGraphXContext));
                var slot = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Marshal.WriteIntPtr(slot, IntPtr.Zero);
                    NativeStatus.ThrowIfFailed(NativeMethods.ContextGetQueue(slot, handle), "migraphx_context_get_queue");
                    return Marshal.ReadIntPtr(slot);
                }
                finally { Marshal.FreeHGlobal(slot); }
            }
        }
    }

    /// <summary>释放保持 program 存活的借用 lease；context 本身由 native program 管理。 Releases the lease that keeps the program alive; the native program owns the context.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            System.Threading.Interlocked.Exchange(ref programLease, null)?.Dispose();
        }
    }
}
