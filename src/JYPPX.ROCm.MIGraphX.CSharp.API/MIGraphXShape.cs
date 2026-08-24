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
            MIGraphXDynamicDimension.ValidateSizeT(copiedLengths[index], nameof(lengths));
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
        // Keep the public dynamic-shape constructor on the same mapped scalar
        // boundary as the static/scalar constructors.  CreateDynamic already
        // validates this value, but the overload taking IReadOnlyList also
        // reaches this private constructor directly.
        ShapeDataTypeMap.ToNative(dataType);
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

    /// <summary>创建标量 shape。 Creates a scalar shape.</summary>
    /// <param name="dataType">标量元素类型。 The scalar element type.</param>
    public static MIGraphXShape CreateScalar(MIGraphXShapeDataType dataType)
        => new MIGraphXShape(dataType, Array.Empty<long>());

    /// <summary>创建显式 stride 的静态 shape。 Creates a static shape with explicit strides.</summary>
    /// <param name="dataType">元素类型。 The element type.</param>
    /// <param name="lengths">每一维正长度；空集合表示标量。 Positive dimension lengths; an empty collection denotes a scalar.</param>
    /// <param name="strides">按元素计的非负 stride。 Non-negative element strides.</param>
    public static MIGraphXShape CreateWithStrides(
        MIGraphXShapeDataType dataType,
        IReadOnlyList<long> lengths,
        IReadOnlyList<long> strides)
    {
        if (lengths is null) { throw new ArgumentNullException(nameof(lengths)); }
        if (strides is null) { throw new ArgumentNullException(nameof(strides)); }
        if (lengths.Count != strides.Count)
        {
            throw new ArgumentException("Shape lengths and strides must have the same rank.", nameof(strides));
        }

        ShapeDataTypeMap.ToNative(dataType);
        var copiedLengths = lengths.ToArray();
        var copiedStrides = strides.ToArray();
        for (var index = 0; index < copiedLengths.Length; index++)
        {
            if (copiedLengths[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lengths), "Static shape lengths must be positive.");
            }
            MIGraphXDynamicDimension.ValidateSizeT(copiedLengths[index], nameof(lengths));
            if (copiedStrides[index] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(strides), "Shape strides must not be negative.");
            }
            MIGraphXDynamicDimension.ValidateSizeT(copiedStrides[index], nameof(strides));
        }

        var elements = ComputeElementCount(copiedLengths);
        var bytes = checked(elements * ShapeDataTypeMap.ElementSize(dataType));
        var standard = copiedStrides.SequenceEqual(CreateStandardStrides(copiedLengths));
        return new MIGraphXShape(dataType, copiedLengths, copiedStrides, elements, bytes, standard);
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

    /// <summary>复制动态维度为独立集合对象。 Creates an independent dynamic-dimension collection snapshot.</summary>
    public MIGraphXDynamicDimensionCollection GetDynamicDimensionCollection() => new MIGraphXDynamicDimensionCollection(dynamicDimensions);

    /// <summary>获取维度数量。 Gets the number of dimensions.</summary>
    public int Rank => IsDynamic ? dynamicDimensions.Length : lengths.Length;

    /// <summary>获取 native <c>migraphx_shape_ndim</c> 的托管别名。 Managed alias for native <c>migraphx_shape_ndim</c>.</summary>
    public int Ndim => Rank;

    /// <summary>获取指定维度长度；对应 native <c>migraphx_shape_index</c> 的值语义。 Gets a dimension length; this is the value semantics of native <c>migraphx_shape_index</c>.</summary>
    /// <param name="index">从零开始的维度索引。 Zero-based dimension index.</param>
    public long GetDimensionLength(int index)
    {
        if (IsDynamic) { throw new InvalidOperationException("A dynamic shape has no concrete dimension lengths."); }
        if ((uint)index >= (uint)lengths.Length) { throw new ArgumentOutOfRangeException(nameof(index)); }
        return lengths[index];
    }

    /// <summary>获取指定维度长度的兼容别名。 Compatibility alias for <see cref="GetDimensionLength"/>.</summary>
    /// <param name="index">维度索引。 Zero-based dimension index.</param>
    public long Index(int index) => GetDimensionLength(index);

    /// <summary>获取经过溢出检查的元素数量。 Gets the overflow-checked element count.</summary>
    public long ElementCount => elementCount ?? throw new InvalidOperationException("ElementCount is not defined for a dynamic shape.");

    /// <summary>获取经过溢出检查的字节数量。 Gets the overflow-checked byte count.</summary>
    public long ByteCount => byteCount ?? throw new InvalidOperationException("ByteCount is not defined for a dynamic shape.");

    /// <summary>获取布局是否为 MIGraphX 标准布局。 Gets whether the layout is MIGraphX-standard.</summary>
    public bool IsStandard { get; }

    /// <summary>获取布局是否无间隙地打包。 Gets whether the layout is packed without gaps.</summary>
    public bool IsPacked { get; }

    /// <summary>复制 shape 元数据；对应 native <c>migraphx_shape_assign_to</c> 的值语义。 Creates an independent shape metadata snapshot.</summary>
    public MIGraphXShape Clone()
    {
        if (IsDynamic) return CreateDynamic(DataType, dynamicDimensions);
        return new MIGraphXShape(DataType, (long[])lengths.Clone(), (long[])strides.Clone(), ElementCount, ByteCount, IsStandard);
    }

    /// <summary>比较完整 shape 元数据；对应 native <c>migraphx_shape_equal</c> 的托管快照语义。 Compares all shape metadata using snapshot value semantics.</summary>
    /// <param name="other">待比较的 shape。 Shape to compare.</param>
    public bool HasSameNativeContent(MIGraphXShape other)
    {
        if (other is null) { throw new ArgumentNullException(nameof(other)); }
        if (DataType != other.DataType || IsDynamic != other.IsDynamic) return false;
        if (IsDynamic) return dynamicDimensions.SequenceEqual(other.dynamicDimensions);
        return lengths.SequenceEqual(other.lengths) && strides.SequenceEqual(other.strides);
    }

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
