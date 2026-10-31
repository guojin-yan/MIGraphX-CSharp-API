using System;
using System.Collections.Generic;
using System.Linq;

namespace JYPPX.ROCm.MIGraphXSharp.Interop;

internal static class ParameterShapeValidation
{
    internal static void RequireMatch(MIGraphXShape expected, MIGraphXShape supplied, string name, string parameterName)
    {
        if (supplied.IsDynamic)
            throw new ArgumentException($"Parameter '{name}' requires a concrete input shape.", parameterName);
        if (expected.DataType != supplied.DataType)
            throw new ArgumentException($"Parameter '{name}' requires {expected.DataType}; supplied {supplied.DataType}.", parameterName);

        // The native scalar constructor represents a managed rank-zero scalar
        // as lengths [1], strides [0]. Compare the shapes actually passed to C.
        var lengths = NativeLengths(supplied);
        if (expected.IsDynamic)
        {
            if (expected.Rank != lengths.Count)
                throw new ArgumentException($"Parameter '{name}' requires rank {expected.Rank}; supplied rank {lengths.Count}.", parameterName);
            var dimensions = expected.DynamicDimensions;
            for (var axis = 0; axis < lengths.Count; axis++)
            {
                var range = dimensions[axis];
                if (lengths[axis] < range.Minimum || lengths[axis] > range.Maximum)
                    throw new ArgumentException($"Parameter '{name}' dimension {axis} must be in [{range.Minimum}, {range.Maximum}]; supplied {lengths[axis]}.", parameterName);
            }
            return;
        }

        var expectedLengths = NativeLengths(expected);
        if (!expectedLengths.SequenceEqual(lengths))
            throw new ArgumentException($"Parameter '{name}' requires lengths [{string.Join(", ", expectedLengths)}]; supplied [{string.Join(", ", lengths)}].", parameterName);
        var expectedStrides = NativeStrides(expected);
        var strides = NativeStrides(supplied);
        if (!expectedStrides.SequenceEqual(strides))
            throw new ArgumentException($"Parameter '{name}' requires strides [{string.Join(", ", expectedStrides)}]; supplied [{string.Join(", ", strides)}].", parameterName);
    }

    private static IReadOnlyList<long> NativeLengths(MIGraphXShape shape)
        => shape.Rank == 0 ? new long[] { 1 } : shape.Lengths;

    private static IReadOnlyList<long> NativeStrides(MIGraphXShape shape)
        => shape.Rank == 0 ? new long[] { 0 } : shape.Strides;
}
