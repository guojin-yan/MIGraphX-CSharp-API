using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>拥有独立 shape 快照的只读集合。 Read-only collection of independent shape snapshots.</summary>
public sealed class MIGraphXShapeCollection : IReadOnlyList<MIGraphXShape>
{
    private readonly MIGraphXShape[] shapes;

    /// <summary>从 shape 序列创建深拷贝集合。 Creates a deep-copy collection from shape values.</summary>
    /// <param name="shapes">shape 序列。 Shape sequence.</param>
    public MIGraphXShapeCollection(IEnumerable<MIGraphXShape> shapes)
    {
        if (shapes is null) throw new ArgumentNullException(nameof(shapes));
        var copied = shapes.ToArray();
        if (copied.Any(value => value is null)) throw new ArgumentException("Shape collections must not contain null values.", nameof(shapes));
        this.shapes = copied.Select(value => value.Clone()).ToArray();
    }

    /// <summary>shape 数量。 Gets the number of shapes.</summary>
    public int Count => shapes.Length;

    /// <summary>按索引获取 shape 快照。 Gets a shape snapshot by index.</summary>
    public MIGraphXShape this[int index] => shapes[index];

    /// <summary>复制集合；对应 native shapes assign-to 的托管快照语义。 Clones the collection using managed snapshot assign-to semantics.</summary>
    public MIGraphXShapeCollection Clone() => new MIGraphXShapeCollection(shapes);

    /// <summary>比较完整 shape 序列。 Compares the complete shape sequence.</summary>
    /// <param name="other">待比较的集合。 Collection to compare.</param>
    public bool HasSameNativeContent(MIGraphXShapeCollection other)
    {
        if (other is null || Count != other.Count) return false;
        for (var index = 0; index < Count; index++)
        {
            if (!this[index].HasSameNativeContent(other[index])) return false;
        }
        return true;
    }

    /// <summary>返回只读枚举器。 Returns a read-only enumerator.</summary>
    public IEnumerator<MIGraphXShape> GetEnumerator() => ((IEnumerable<MIGraphXShape>)shapes).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
