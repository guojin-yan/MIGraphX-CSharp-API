using System;
using System.Collections.Generic;
using System.Linq;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示一个不可变的 MIGraphX 动态维度范围。
/// Represents an immutable MIGraphX dynamic-dimension range.
/// </summary>
public sealed class MIGraphXDynamicDimension : IEquatable<MIGraphXDynamicDimension>
{
    private readonly long[] optimals;

    /// <summary>创建闭区间动态维度，可选 optimal 值必须位于区间内且严格递增。 Creates a closed range; optional optimal values must be in range and strictly increasing.</summary>
    public MIGraphXDynamicDimension(long minimum, long maximum, IReadOnlyList<long>? optimals = null)
    {
        ValidateSizeT(minimum, nameof(minimum));
        ValidateSizeT(maximum, nameof(maximum));
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "The minimum dynamic dimension must not exceed the maximum.");
        }

        var copied = optimals is null ? Array.Empty<long>() : optimals.ToArray();
        for (var index = 0; index < copied.Length; index++)
        {
            ValidateSizeT(copied[index], nameof(optimals));
            if (copied[index] < minimum || copied[index] > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(optimals), "Every optimal value must be within the dimension range.");
            }
            if (index > 0 && copied[index] <= copied[index - 1])
            {
                throw new ArgumentException("Optimal values must be strictly increasing and must not repeat.", nameof(optimals));
            }
        }

        Minimum = minimum;
        Maximum = maximum;
        this.optimals = copied;
    }

    /// <summary>创建固定维度。 Creates a fixed dimension.</summary>
    public MIGraphXDynamicDimension(long value)
        : this(value, value)
    {
    }

    /// <summary>创建一个固定维度。 Creates a fixed dimension.</summary>
    /// <param name="value">固定值。 The fixed value.</param>
    public static MIGraphXDynamicDimension Fixed(long value) => new MIGraphXDynamicDimension(value);

    /// <summary>创建范围维度。 Creates a range dimension.</summary>
    /// <param name="minimum">最小值。 The minimum value.</param>
    /// <param name="maximum">最大值。 The maximum value.</param>
    /// <param name="optimals">可选 optimal 值。 Optional optimal values.</param>
    public static MIGraphXDynamicDimension Range(long minimum, long maximum, IReadOnlyList<long>? optimals = null) => new MIGraphXDynamicDimension(minimum, maximum, optimals);

    /// <summary>比较两个动态维度。 Compares two dynamic dimensions.</summary>
    /// <param name="left">左值。 The left value.</param>
    /// <param name="right">右值。 The right value.</param>
    public static bool operator ==(MIGraphXDynamicDimension? left, MIGraphXDynamicDimension? right) => ReferenceEquals(left, right) || left is not null && left.Equals(right);
    /// <summary>比较两个动态维度是否不等。 Compares two dynamic dimensions for inequality.</summary>
    /// <param name="left">左值。 The left value.</param>
    /// <param name="right">右值。 The right value.</param>
    public static bool operator !=(MIGraphXDynamicDimension? left, MIGraphXDynamicDimension? right) => !(left == right);

    /// <summary>获取最小值。 Gets the minimum value.</summary>
    public long Minimum { get; }

    /// <summary>获取最大值。 Gets the maximum value.</summary>
    public long Maximum { get; }

    /// <summary>获取 optional optimal 值的不可变副本。 Gets an immutable copy of optional optimal values.</summary>
    public IReadOnlyList<long> Optimals => Array.AsReadOnly(optimals);

    /// <summary>获取该维度是否固定。 Gets whether this dimension is fixed.</summary>
    public bool IsFixed => Minimum == Maximum;

    /// <summary>获取一个不带 native 读值证据的显式未知范围。 Gets an explicit unknown range used when the C API exposes no dimension getters.</summary>
    public static MIGraphXDynamicDimension Unknown => new MIGraphXDynamicDimension(0, UIntPtr.Size == 4 ? uint.MaxValue : long.MaxValue);

    /// <summary>比较动态维度值。 Compares dynamic-dimension values.</summary>
    /// <param name="other">待比较值。 The value to compare.</param>
    public bool Equals(MIGraphXDynamicDimension? other)
    {
        if (ReferenceEquals(this, other)) { return true; }
        if (other is null || Minimum != other.Minimum || Maximum != other.Maximum) { return false; }
        return optimals.SequenceEqual(other.optimals);
    }

    /// <summary>比较对象值。 Compares object value semantics.</summary>
    /// <param name="obj">待比较对象。 The object to compare.</param>
    public override bool Equals(object? obj) => Equals(obj as MIGraphXDynamicDimension);

    /// <summary>获取稳定 hash。 Gets a stable hash code.</summary>
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (Minimum.GetHashCode() * 397) ^ Maximum.GetHashCode();
            for (var index = 0; index < optimals.Length; index++) { hash = (hash * 397) ^ optimals[index].GetHashCode(); }
            return hash;
        }
    }

    /// <summary>获取范围文本。 Gets a range representation.</summary>
    public override string ToString() => IsFixed ? Minimum.ToString() : $"{Minimum}..{Maximum}";

    internal static void ValidateSizeT(long value, string parameterName)
    {
        if (value < 0 || (UIntPtr.Size == 4 && value > uint.MaxValue))
        {
            throw new OverflowException($"The {parameterName} value does not fit the current native size_t range.");
        }
    }

    internal static UIntPtr ToNativeSizeT(long value, string parameterName)
    {
        ValidateSizeT(value, parameterName);
        return UIntPtr.Size == 4 ? new UIntPtr((uint)value) : new UIntPtr((ulong)value);
    }
}
