using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示一个不可变的 MIGraphX 动态维度范围。
/// Represents an immutable MIGraphX dynamic-dimension range.
/// </summary>
public sealed class MIGraphXDynamicDimension : IEquatable<MIGraphXDynamicDimension>
{
    private readonly long[] optimals;
    private readonly IReadOnlyDictionary<string, MIGraphXDynamicDimension> symbols = new System.Collections.ObjectModel.ReadOnlyDictionary<string, MIGraphXDynamicDimension>(new Dictionary<string, MIGraphXDynamicDimension>());

    /// <summary>符号表达式，普通范围为 null。 Symbolic expression, or null for a numeric range.</summary>
    public string? Expression { get; }

    /// <summary>表达式使用的不可变符号范围。 Immutable symbol bounds used by the expression.</summary>
    public IReadOnlyDictionary<string, MIGraphXDynamicDimension> Symbols => symbols;

    /// <summary>是否为符号表达式。 Whether this is a symbolic expression.</summary>
    public bool IsSymbolic => Expression is not null;

    private MIGraphXDynamicDimension(string expression, IReadOnlyDictionary<string, MIGraphXDynamicDimension> symbols)
        : this(0, UIntPtr.Size == 4 ? uint.MaxValue : long.MaxValue)
    {
        using var text = new StrictUtf8String(expression, nameof(expression));
        if (symbols is null) throw new ArgumentNullException(nameof(symbols));
        var copied = new Dictionary<string, MIGraphXDynamicDimension>(StringComparer.Ordinal);
        foreach (var pair in symbols)
        {
            using var name = new StrictUtf8String(pair.Key, nameof(symbols));
            if (pair.Value is null || pair.Value.IsSymbolic) throw new ArgumentException("Symbol bounds must be numeric ranges.", nameof(symbols));
            copied.Add(pair.Key, pair.Value.Clone());
        }
        Expression = expression;
        this.symbols = new System.Collections.ObjectModel.ReadOnlyDictionary<string, MIGraphXDynamicDimension>(copied);
    }

    /// <summary>创建 2.18 符号维度；语法与实际范围由原生库求值，托管最小/最大值为保守未知范围。 Creates a 2.18 symbolic dimension; native code evaluates syntax and bounds, while managed minimum/maximum remain conservative unknown bounds.</summary>
    /// <param name="expression">MIGraphX 表达式。 MIGraphX expression.</param>
    /// <param name="symbols">命名数值范围，禁止递归符号。 Named numeric bounds; recursive symbols are rejected.</param>
    public static MIGraphXDynamicDimension Symbolic(string expression, IReadOnlyDictionary<string, MIGraphXDynamicDimension> symbols)
        => new MIGraphXDynamicDimension(expression, symbols);

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
    public bool IsFixed => !IsSymbolic && Minimum == Maximum;

    /// <summary>获取一个不带 native 读值证据的显式未知范围。 Gets an explicit unknown range used when the C API exposes no dimension getters.</summary>
    public static MIGraphXDynamicDimension Unknown => new MIGraphXDynamicDimension(0, UIntPtr.Size == 4 ? uint.MaxValue : long.MaxValue);

    /// <summary>比较动态维度值。 Compares dynamic-dimension values.</summary>
    /// <param name="other">待比较值。 The value to compare.</param>
    public bool Equals(MIGraphXDynamicDimension? other)
    {
        if (ReferenceEquals(this, other)) { return true; }
        if (other is null || Minimum != other.Minimum || Maximum != other.Maximum || Expression != other.Expression) { return false; }
        return optimals.SequenceEqual(other.optimals) && symbols.Count == other.symbols.Count
            && symbols.All(pair => other.symbols.TryGetValue(pair.Key, out var value) && pair.Value.Equals(value));
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
            if (Expression is not null) hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Expression);
            foreach (var pair in symbols.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                hash = ((hash * 397) ^ StringComparer.Ordinal.GetHashCode(pair.Key)) * 397 ^ pair.Value.GetHashCode();
            return hash;
        }
    }

    /// <summary>复制动态维度值；对应 native assign-to 的托管值语义。 Copies this dynamic-dimension value.</summary>
    public MIGraphXDynamicDimension Clone() => Expression is null ? new MIGraphXDynamicDimension(Minimum, Maximum, optimals) : Symbolic(Expression, symbols);

    /// <summary>比较完整动态维度值；对应 native dynamic-dimension equality 的托管快照语义。</summary>
    /// <param name="other">待比较的动态维度。 Dynamic dimension to compare.</param>
    public bool HasSameNativeContent(MIGraphXDynamicDimension other)
        => other is not null && Equals(other);

    /// <summary>获取范围文本。 Gets a range representation.</summary>
    public override string ToString() => Expression ?? (IsFixed
        ? Minimum.ToString(CultureInfo.InvariantCulture)
        : Minimum.ToString(CultureInfo.InvariantCulture) + ".." + Maximum.ToString(CultureInfo.InvariantCulture));

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
