using System.Collections.Generic;

namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>
/// 表示受限 M2 单输入、单输出同步 ONNX 执行的托管快照。
/// Represents a managed snapshot from the restricted M2 single-input, single-output synchronous ONNX execution.
/// </summary>
public sealed class MIGraphXOnnxExecutionResult
{
    internal MIGraphXOnnxExecutionResult(string inputName, long[] inputDimensions, long[] outputDimensions, float[] output)
    {
        InputName = inputName;
        InputDimensions = System.Array.AsReadOnly((long[])inputDimensions.Clone());
        OutputDimensions = System.Array.AsReadOnly((long[])outputDimensions.Clone());
        Output = System.Array.AsReadOnly((float[])output.Clone());
    }

    /// <summary>获取模型输入名称。 Gets the model input name.</summary>
    public string InputName { get; }

    /// <summary>获取静态输入维度快照。 Gets the static input-dimension snapshot.</summary>
    public IReadOnlyList<long> InputDimensions { get; }

    /// <summary>获取静态输出维度快照。 Gets the static output-dimension snapshot.</summary>
    public IReadOnlyList<long> OutputDimensions { get; }

    /// <summary>获取在原生输出集合释放前复制的 float32 输出。 Gets the float32 output copied before the native output collection was released.</summary>
    public IReadOnlyList<float> Output { get; }
}
