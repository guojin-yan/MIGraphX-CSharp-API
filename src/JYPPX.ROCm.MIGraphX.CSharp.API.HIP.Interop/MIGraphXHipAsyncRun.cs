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
    private readonly bool hasHostOutputs;
    private bool disposed;

    internal MIGraphXHipAsyncRun(HipStream stream, MIGraphXNativeAsyncRun nativeRun, bool hasHostOutputs = true)
    {
        this.stream = stream;
        this.nativeRun = nativeRun;
        this.hasHostOutputs = hasHostOutputs;
    }

    /// <summary>获取完成回调是否已结束执行并释放提交租约；此属性不查询 stream。 Gets whether completion finalized execution and released submission leases; this property does not query the stream.</summary>
    public bool IsCompleted { get { lock (sync) { ThrowIfDisposed(); return nativeRun.IsCompleted; } } }

    /// <summary>获取完成后的 owned host outputs；完成前或 RunDeviceIntoAsync 路径访问失败。 Gets owned host outputs after completion; unavailable before completion or for RunDeviceIntoAsync.</summary>
    public MIGraphXArgumentCollection Outputs { get { lock (sync) { ThrowIfDisposed(); if (!hasHostOutputs) throw new InvalidOperationException("This run writes device destinations; no host outputs were requested."); return nativeRun.Outputs; } } }

    /// <summary>非阻塞查询 stream，完成时清理执行并按所选模式固化主机输出或报告错误。 Queries the stream without blocking, finalizes execution and, when requested, snapshots host outputs or reports errors.</summary>
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

    /// <summary>阻塞等待 stream，完成执行并报告 HIP 或输出复制错误。 Blocks for the stream, finalizes execution and reports HIP or output-copy errors.</summary>
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
