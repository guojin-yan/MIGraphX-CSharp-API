using System;
using System.Collections.Generic;
using System.Linq;
using JYPPX.ROCm.MIGraphXSharp.Interop;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示不依赖原生集合生命周期的不可变静态 shape 快照。
/// Represents an immutable static-shape snapshot independent of native collection lifetimes.
/// </summary>
public sealed class MIGraphXShape
{
    private readonly long[] lengths;
    private readonly long[] strides;
    private readonly MIGraphXDynamicDimension[] dynamicDimensions;
    private readonly long? elementCount;
    private readonly long? byteCount;

    /// <summary>
    /// 创建采用标准行主序布局的静态 shape 元数据。
    /// Creates static shape metadata with standard row-major layout.
    /// </summary>
    /// <param name="dataType">已映射的标量元素类型。 The mapped scalar element type.</param>
    /// <param name="lengths">每一维的正长度；空集合表示标量。 Positive lengths for each dimension; an empty collection denotes a scalar.</param>
    public MIGraphXShape(MIGraphXShapeDataType dataType, IReadOnlyList<long> lengths)
    {
        if (lengths is null) { throw new ArgumentNullException(nameof(lengths)); }
        ShapeDataTypeMap.ToNative(dataType);
        var copiedLengths = lengths.ToArray();
        for (var index = 0; index < copiedLengths.Length; index++)
        {
            if (copiedLengths[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lengths), "Static M4 shape lengths must be positive.");
            }
        }

        var copiedStrides = CreateStandardStrides(copiedLengths);
        var elements = ComputeElementCount(copiedLengths);
        var bytes = checked(elements * ShapeDataTypeMap.ElementSize(dataType));
        DataType = dataType;
        this.lengths = copiedLengths;
        strides = copiedStrides;
        elementCount = elements;
        byteCount = bytes;
        IsStandard = true;
        IsPacked = true;
        dynamicDimensions = Array.Empty<MIGraphXDynamicDimension>();
    }

    /// <summary>创建动态 shape 值。 Creates dynamic shape values.</summary>
    /// <param name="dataType">元素类型。 The element type.</param>
    /// <param name="dimensions">动态维度范围。 The dynamic dimension ranges.</param>
    public MIGraphXShape(MIGraphXShapeDataType dataType, IReadOnlyList<MIGraphXDynamicDimension> dimensions)
        : this(dataType, CopyDynamicDimensions(dimensions))
    {
    }

    private MIGraphXShape(MIGraphXShapeDataType dataType, MIGraphXDynamicDimension[] dimensions)
    {
        DataType = dataType;
        dynamicDimensions = dimensions;
        lengths = Array.Empty<long>();
        strides = Array.Empty<long>();
        elementCount = null;
        byteCount = null;
        IsDynamic = true;
        IsStandard = false;
        IsPacked = false;
    }

    private MIGraphXShape(
        MIGraphXShapeDataType dataType,
        long[] lengths,
        long[] strides,
        long elementCount,
        long byteCount,
        bool isStandard)
    {
        DataType = dataType;
        this.lengths = lengths;
        this.strides = strides;
        this.elementCount = elementCount;
        this.byteCount = byteCount;
        IsStandard = isStandard;
        IsPacked = ComputePacked(lengths, strides);
        dynamicDimensions = Array.Empty<MIGraphXDynamicDimension>();
    }

    /// <summary>创建动态 shape。 Creates a dynamic shape.</summary>
    /// <param name="dataType">元素类型。 The element type.</param>
    /// <param name="dimensions">动态维度范围。 The dynamic dimension ranges.</param>
    public static MIGraphXShape CreateDynamic(MIGraphXShapeDataType dataType, IReadOnlyList<MIGraphXDynamicDimension> dimensions)
    {
        if (dimensions is null) { throw new ArgumentNullException(nameof(dimensions)); }
        ShapeDataTypeMap.ToNative(dataType);
        var copied = dimensions.ToArray();
        for (var index = 0; index < copied.Length; index++)
        {
            if (copied[index] is null) { throw new ArgumentException("Dynamic dimensions must not contain null values.", nameof(dimensions)); }
        }
        return new MIGraphXShape(dataType, copied);
    }

    /// <summary>获取已映射的标量元素类型。 Gets the mapped scalar element type.</summary>
    public MIGraphXShapeDataType DataType { get; }

    /// <summary>获取不可变维度长度快照。 Gets the immutable dimension-length snapshot.</summary>
    public IReadOnlyList<long> Lengths
    {
        get
        {
            if (IsDynamic) { throw new InvalidOperationException("Lengths are not defined for a dynamic shape; inspect DynamicDimensions instead."); }
            return Array.AsReadOnly(lengths);
        }
    }

    /// <summary>获取不可变 stride 快照。 Gets the immutable stride snapshot.</summary>
    public IReadOnlyList<long> Strides
    {
        get
        {
            if (IsDynamic) { throw new InvalidOperationException("Strides are not defined for a dynamic shape."); }
            return Array.AsReadOnly(strides);
        }
    }

    /// <summary>获取是否为动态 shape。 Gets whether this is a dynamic shape.</summary>
    public bool IsDynamic { get; }

    /// <summary>获取动态维度范围；静态 shape 返回空集合。 Gets dynamic dimension ranges; static shapes return an empty collection.</summary>
    public IReadOnlyList<MIGraphXDynamicDimension> DynamicDimensions => Array.AsReadOnly(dynamicDimensions);

    /// <summary>获取维度数量。 Gets the number of dimensions.</summary>
    public int Rank => IsDynamic ? dynamicDimensions.Length : lengths.Length;

    /// <summary>获取经过溢出检查的元素数量。 Gets the overflow-checked element count.</summary>
    public long ElementCount => elementCount ?? throw new InvalidOperationException("ElementCount is not defined for a dynamic shape.");

    /// <summary>获取经过溢出检查的字节数量。 Gets the overflow-checked byte count.</summary>
    public long ByteCount => byteCount ?? throw new InvalidOperationException("ByteCount is not defined for a dynamic shape.");

    /// <summary>获取布局是否为 MIGraphX 标准布局。 Gets whether the layout is MIGraphX-standard.</summary>
    public bool IsStandard { get; }

    /// <summary>获取布局是否无间隙地打包。 Gets whether the layout is packed without gaps.</summary>
    public bool IsPacked { get; }

    internal long[] CopyLengths()
    {
        if (IsDynamic) { throw new InvalidOperationException("A dynamic shape has no concrete lengths."); }
        return (long[])lengths.Clone();
    }

    internal static MIGraphXShape FromNative(IntPtr shape, string context, IReadOnlyList<MIGraphXDynamicDimension>? dynamicFallback = null)
    {
        var snapshot = NativeShapeSnapshot.Create(shape, context);
        if (snapshot.Dynamic)
        {
            if (dynamicFallback is null)
            {
                throw new NotSupportedException("A native dynamic shape requires an explicit managed override to provide dimension ranges.");
            }
            if (dynamicFallback.Count != snapshot.DynamicFixedFlags.Length)
            {
                throw new InvalidOperationException("The managed dynamic override rank does not match the native shape.");
            }
            var dimensions = dynamicFallback.ToArray();
            return CreateDynamic(ShapeDataTypeMap.FromNative(snapshot.Type), dimensions);
        }

        var dataType = ShapeDataTypeMap.FromNative(snapshot.Type);
        if (snapshot.Dimensions.Any(length => length <= 0))
        {
            throw new NotSupportedException("M4 static shape lengths must be positive.");
        }
        if (snapshot.Dimensions.Length != snapshot.Strides.Length)
        {
            throw new InvalidOperationException("Native shape length and stride ranks differ.");
        }

        var expectedElements = ComputeElementCount(snapshot.Dimensions);
        if (snapshot.Elements != expectedElements)
        {
            throw new InvalidOperationException("Native shape element count does not match its lengths.");
        }

        var expectedBytes = checked(expectedElements * ShapeDataTypeMap.ElementSize(dataType));
        if (snapshot.Bytes != expectedBytes)
        {
            throw new InvalidOperationException("Native shape byte count does not match its mapped element type.");
        }

        return new MIGraphXShape(
            dataType,
            (long[])snapshot.Dimensions.Clone(),
            (long[])snapshot.Strides.Clone(),
            snapshot.Elements,
            snapshot.Bytes,
            snapshot.Standard);
    }

    private static long[] CreateStandardStrides(long[] dimensions)
    {
        var result = new long[dimensions.Length];
        var stride = 1L;
        for (var index = dimensions.Length - 1; index >= 0; index--)
        {
            result[index] = stride;
            stride = checked(stride * dimensions[index]);
        }
        return result;
    }

    private static MIGraphXDynamicDimension[] CopyDynamicDimensions(IReadOnlyList<MIGraphXDynamicDimension> dimensions)
    {
        if (dimensions is null) { throw new ArgumentNullException(nameof(dimensions)); }
        var copied = dimensions.ToArray();
        if (copied.Any(value => value is null)) { throw new ArgumentException("Dynamic dimensions must not contain null values.", nameof(dimensions)); }
        return copied;
    }

    private static long ComputeElementCount(long[] dimensions)
    {
        var result = 1L;
        for (var index = 0; index < dimensions.Length; index++)
        {
            result = checked(result * dimensions[index]);
        }
        return result;
    }

    private static bool ComputePacked(long[] dimensions, long[] layoutStrides)
    {
        var axes = Enumerable.Range(0, dimensions.Length)
            .Where(index => dimensions[index] > 1)
            .OrderBy(index => layoutStrides[index])
            .ToArray();
        var expected = 1L;
        foreach (var axis in axes)
        {
            if (layoutStrides[axis] != expected)
            {
                return false;
            }
            expected = checked(expected * dimensions[axis]);
        }
        return true;
    }
}
