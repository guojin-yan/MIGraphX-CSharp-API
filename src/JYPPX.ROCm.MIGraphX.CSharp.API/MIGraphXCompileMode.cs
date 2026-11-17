namespace JYPPX.ROCm.MIGraphXSharp;

/// <summary>MIGraphX 2.18 编译策略。 MIGraphX 2.18 compilation strategy.</summary>
public enum MIGraphXCompileMode : sbyte
{
    /// <summary>尽早编译。 Eager compilation.</summary>
    Eager = 0,
    /// <summary>平衡编译开销与性能。 Balances compilation cost and performance.</summary>
    Balanced = 50,
    /// <summary>最大化优化。 Maximizes optimization.</summary>
    Max = 100,
}
