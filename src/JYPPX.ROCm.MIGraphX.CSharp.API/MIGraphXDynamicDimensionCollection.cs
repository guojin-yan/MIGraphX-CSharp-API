using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>拥有独立动态维度值的只读集合。 Read-only collection of independent dynamic-dimension values.</summary>
public sealed class MIGraphXDynamicDimensionCollection : IReadOnlyList<MIGraphXDynamicDimension>
{
    private readonly MIGraphXDynamicDimension[] dimensions;

    /// <summary>从动态维度序列创建深拷贝集合。 Creates a deep-copy collection from dynamic-dimension values.</summary>
    /// <param name="dimensions">动态维度序列。 Dynamic-dimension sequence.</param>
    public MIGraphXDynamicDimensionCollection(IEnumerable<MIGraphXDynamicDimension> dimensions)
    {
        if (dimensions is null) throw new ArgumentNullException(nameof(dimensions));
        var copied = dimensions.ToArray();
        if (copied.Any(value => value is null)) throw new ArgumentException("Dynamic-dimension collections must not contain null values.", nameof(dimensions));
        this.dimensions = copied.Select(value => value.Clone()).ToArray();
    }

    /// <summary>动态维度数量。 Gets the number of dimensions.</summary>
    public int Count => dimensions.Length;

    /// <summary>按索引获取动态维度快照。 Gets a dynamic-dimension snapshot by index.</summary>
    public MIGraphXDynamicDimension this[int index] => dimensions[index];

    /// <summary>复制集合；对应 native dynamic-dimensions assign-to 的托管值语义。 Clones the collection using managed assign-to value semantics.</summary>
    public MIGraphXDynamicDimensionCollection Clone() => new MIGraphXDynamicDimensionCollection(dimensions);

    /// <summary>比较完整动态维度序列。 Compares the complete dynamic-dimension sequence.</summary>
    /// <param name="other">待比较的集合。 Collection to compare.</param>
    public bool HasSameNativeContent(MIGraphXDynamicDimensionCollection other)
    {
        if (other is null || Count != other.Count) return false;
        for (var index = 0; index < Count; index++)
        {
            if (!this[index].HasSameNativeContent(other[index])) return false;
        }
        return true;
    }

    /// <summary>返回只读枚举器。 Returns a read-only enumerator.</summary>
    public IEnumerator<MIGraphXDynamicDimension> GetEnumerator() => ((IEnumerable<MIGraphXDynamicDimension>)dimensions).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
