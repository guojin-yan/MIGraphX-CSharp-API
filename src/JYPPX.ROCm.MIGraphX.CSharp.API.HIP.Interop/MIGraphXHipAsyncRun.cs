using System;
using JYPPX.ROCm.HipSharp.Streams;
using JYPPX.ROCm.MIGraphXSharp;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop;

/// <summary>表示在 HipSharp stream 上提交且由 stream 完成边界驱动的 MIGraphX native 异步执行。 Represents a MIGraphX native asynchronous run driven by a HipSharp stream completion boundary.</summary>
public sealed class MIGraphXHipAsyncRun : IDisposable
{
    private readonly object sync = new object();
    private readonly HipStream stream;
    private readonly MIGraphXNativeAsyncRun nativeRun;
    private bool disposed;

    internal MIGraphXHipAsyncRun(HipStream stream, MIGraphXNativeAsyncRun nativeRun)
    {
        this.stream = stream;
        this.nativeRun = nativeRun;
    }

    /// <summary>获取完成回调是否已经固化 owned host outputs；此属性不查询 stream。 Gets whether completion copied owned host outputs; this property does not query the stream.</summary>
    public bool IsCompleted { get { lock (sync) { ThrowIfDisposed(); return nativeRun.IsCompleted; } } }

    /// <summary>获取完成后由此结果拥有的 host output 集合；完成前访问失败。 Gets the owned host-output collection after completion; access before completion fails.</summary>
    public MIGraphXArgumentCollection Outputs { get { lock (sync) { ThrowIfDisposed(); return nativeRun.Outputs; } } }

    /// <summary>非阻塞查询 stream，并在完成时固化 outputs 或报告完成错误。 Queries the stream without blocking and snapshots outputs or reports a completion error when ready.</summary>
    /// <returns>stream 和此次执行是否已经完成。 Whether the stream and this run completed.</returns>
    public bool TryComplete()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (!nativeRun.IsCompleted && !stream.Query()) return false;
            nativeRun.ThrowIfFailed();
            return true;
        }
    }

    /// <summary>阻塞等待 stream，固化 outputs，并报告 HIP 或 output-copy 错误。 Blocks for the stream, snapshots outputs, and reports HIP or output-copy errors.</summary>
    public void Synchronize()
    {
        lock (sync)
        {
            ThrowIfDisposed();
            if (!nativeRun.IsCompleted) stream.Synchronize();
            nativeRun.ThrowIfFailed();
        }
    }

    /// <summary>未完成时先阻塞同步，再释放 owned outputs；重复调用安全。 Blocks to synchronize when needed, then releases owned outputs; repeated calls are safe.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            if (!nativeRun.IsCompleted) stream.Synchronize();
            try
            {
                nativeRun.ThrowIfFailed();
            }
            catch
            {
                // Completion errors are already fully cleaned by the core state. Preserve
                // the primary error while making result disposal idempotent.
                if (nativeRun.IsCompleted)
                {
                    nativeRun.Dispose();
                    disposed = true;
                }
                throw;
            }
            nativeRun.Dispose();
            disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(MIGraphXHipAsyncRun));
    }
}
