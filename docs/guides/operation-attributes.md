# Typed operation attributes

`MIGraphXOperationAttributes` builds ordinary managed values for operation attributes. The named factories cover `reshape`, `transpose`, `slice`, `multibroadcast`, and `topk`. The general builder accepts integral, finite floating-point, Boolean, string, null, and array values, including Boolean arrays. It materializes the complete attribute text before calling native code; arbitrary C format placeholders and C variadic values are excluded.

```csharp
var attributes = MIGraphXOperationAttributes.ForReshape(new long[] { 1, 4 });
using var operation = MIGraphXOperation.Create(nativePath, "reshape", attributes);
using var clone = operation.Clone();
```

Use attributes valid for the selected native operation and model shape. Managed serialization does not establish every operation's semantics on every target.

Inspect all five named forms without loading the native library:

```csharp
using JYPPX.ROCm.MIGraphXSharp;

Console.WriteLine(MIGraphXOperationAttributes.ForReshape(1, 4).Build());
Console.WriteLine(MIGraphXOperationAttributes.ForTranspose(1, 0).Build());
Console.WriteLine(MIGraphXOperationAttributes.ForSlice(
    new long[] { 0 }, new long[] { 0 }, new long[] { 1 }).Build());
Console.WriteLine(MIGraphXOperationAttributes.ForMultibroadcast(1, 4).Build());
Console.WriteLine(MIGraphXOperationAttributes.ForTopK(1, 1, true).Build());
```

Creating and cloning operations with `MIGraphXOperation.Create` requires an absolute path to the MIGraphX C library on a compatible Linux host. Arbitrary format placeholders, unmanaged pointers, and general C variadic values remain outside the managed API boundary.

中文说明：上述代码覆盖 reshape、transpose、slice、multibroadcast、topk 五种属性形态。调用 `Build()` 只执行托管序列化；通过 `MIGraphXOperation.Create` 创建原生算子时才需要匹配的运行环境。任意 format placeholder、非托管指针和通用 C 可变参数仍不在托管 API 边界内。
