using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示按原生输出索引排序并拥有各输出 argument 的只读集合。
/// Represents a read-only collection ordered by native output index and owning each output argument.
/// </summary>
public sealed class MIGraphXArgumentCollection : IReadOnlyList<MIGraphXArgument>, IDisposable
{
    private readonly object sync = new object();
    private MIGraphXArgument[]? arguments;

    internal MIGraphXArgumentCollection(IEnumerable<MIGraphXArgument> arguments)
    {
        this.arguments = arguments.ToArray();
    }

    /// <summary>获取输出数量；Dispose 后访问会失败。 Gets the output count; access fails after disposal.</summary>
    public int Count
    {
        get { lock (sync) { return RequireArguments().Length; } }
    }

    /// <summary>按确定性原生索引获取 owned 输出。 Gets an owned output by deterministic native index.</summary>
    /// <param name="index">从零开始的输出索引。 Zero-based output index.</param>
    public MIGraphXArgument this[int index]
    {
        get { lock (sync) { return RequireArguments()[index]; } }
    }

    /// <summary>复制集合及其所有 argument；对应 native <c>migraphx_arguments_assign_to</c> 的托管快照语义。 Creates a managed snapshot with cloned arguments.</summary>
    public MIGraphXArgumentCollection Clone()
    {
        lock (sync)
        {
            var current = RequireArguments();
            var copies = new MIGraphXArgument[current.Length];
            try
            {
                for (var index = 0; index < current.Length; index++) copies[index] = current[index].Clone();
                return new MIGraphXArgumentCollection(copies);
            }
            catch
            {
                foreach (var copy in copies) copy?.Dispose();
                throw;
            }
        }
    }

    /// <summary>返回当前只读输出快照的枚举器。 Returns an enumerator over the current read-only output snapshot.</summary>
    /// <returns>按原生索引排序的枚举器。 An enumerator ordered by native index.</returns>
    public IEnumerator<MIGraphXArgument> GetEnumerator()
    {
        lock (sync)
        {
            return ((IEnumerable<MIGraphXArgument>)RequireArguments().Clone()).GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>按索引顺序释放所有 owned 输出；重复调用安全。 Releases every owned output in index order; repeated calls are safe.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            var owned = arguments;
            arguments = null;
            if (owned is null) { return; }
            foreach (var argument in owned)
            {
                argument.Dispose();
            }
        }
    }

    private MIGraphXArgument[] RequireArguments() => arguments ?? throw new ObjectDisposedException(nameof(MIGraphXArgumentCollection));
}
