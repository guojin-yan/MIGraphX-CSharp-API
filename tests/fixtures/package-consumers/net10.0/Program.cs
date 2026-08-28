using JYPPX.ROCm.MIGraphXSharp;
using System.Linq;

internal static class Program
{
    private static int Main()
    {
        var shape = new MIGraphXShape(MIGraphXShapeDataType.Float32, new long[] { 1, 4 });
        var scalar = MIGraphXShape.CreateScalar(MIGraphXShapeDataType.Float32);
        var strided = MIGraphXShape.CreateWithStrides(MIGraphXShapeDataType.Float32, new long[] { 2, 2 }, new long[] { 1, 2 });
        var dynamicDimension = MIGraphXDynamicDimension.Range(1, 8, new long[] { 4 });
        var dynamicShape = MIGraphXShape.CreateDynamic(MIGraphXShapeDataType.Float32, new[] { dynamicDimension });
        var reshape = MIGraphXOperationAttributes.ForReshape(1, 4);
        var transpose = MIGraphXOperationAttributes.ForTranspose(1, 0);
        var slice = MIGraphXOperationAttributes.ForSlice(new long[] { 0 }, new long[] { 0 }, new long[] { 1 });
        var multibroadcast = MIGraphXOperationAttributes.ForMultibroadcast(1, 4);
        var topk = MIGraphXOperationAttributes.ForTopK(1, 1, true);
        var flags = new MIGraphXOperationAttributes().SetBooleanArray("flags", new[] { true, false });
        var typedScalars = new MIGraphXOperationAttributes().SetUInt32("u32", 3u).SetInt64("i64", -4L).SetUInt64("u64", 5UL).SetDouble("double", 1.25);
        var typedArrays = new MIGraphXOperationAttributes().SetInt32Array("i32s", new[] { -1, 2 }).SetUInt32Array("u32s", new[] { 3u, 4u }).SetUInt64Array("u64s", new[] { 5UL, 6UL }).SetSingleArray("singles", new[] { 0.5f, -1f }).SetDoubleArray("doubles", new[] { 1.25, 2.5 }).SetStringArray("labels", new[] { "a", "b" });
        var metadata = new MIGraphXCacheMetadata(
            new string('a', 64), "gpu", "offloadCopy=true", "msgpack", new string('b', 64),
            new[] { new MIGraphXCacheOverride("input", new[] { dynamicDimension }) });
        var m12PublicSurface =
            typeof(MIGraphXArgument) != null
            && typeof(MIGraphXModule) != null
            && typeof(MIGraphXInstruction) != null
            && typeof(MIGraphXInstructions) != null
            && typeof(MIGraphXModules) != null
            && typeof(MIGraphXOperation) != null
            && typeof(MIGraphXTfOptions) != null
            && typeof(MIGraphXQuantizeOpNames) != null
            && typeof(MIGraphXQuantizeInt8Options) != null
            && typeof(MIGraphXQuantizeFp8Options) != null
            && typeof(MIGraphXContext) != null
            && typeof(MIGraphXExperimentalCustomOp) != null;
        return MIGraphXBuildInfo.NativeBindingsAvailable
            && MIGraphXStatus.Success == 0
            && shape.ElementCount == 4
            && shape.ByteCount == 16
            && shape.IsPacked
            && scalar.Rank == 0
            && scalar.ElementCount == 1
            && strided.Rank == 2
            && strided.Strides.SequenceEqual(new long[] { 1, 2 })
            && dynamicShape.IsDynamic
            && dynamicShape.DynamicDimensions[0].Equals(dynamicDimension)
            && reshape.Build() == "{dims: [1, 4]}"
            && transpose.Build() == "{permutation: [1, 0]}"
            && slice.Build() == "{axes: [0], starts: [0], ends: [1]}"
            && multibroadcast.Build() == "{out_lens: [1, 4]}"
            && topk.Build() == "{axis: 1, k: 1, largest: true}"
            && flags.Build() == "{flags: [true, false]}"
            && typedScalars.Build() == "{u32: 3, i64: -4, u64: 5, double: 1.25}"
            && typedArrays.Build() == "{i32s: [-1, 2], u32s: [3, 4], u64s: [5, 6], singles: [0.5, -1], doubles: [1.25, 2.5], labels: [\"a\", \"b\"]}"
            && metadata.ComputeKey().Length == 64
            && m12PublicSurface
            && typeof(MIGraphXProgram).Assembly == typeof(MIGraphXBuildInfo).Assembly ? 0 : 1;
    }
}
