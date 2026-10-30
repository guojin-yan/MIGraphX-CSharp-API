# Typed operation attributes

`MIGraphXOperationAttributes` builds ordinary managed values for operation attributes. The named factories cover `reshape`, `transpose`, `slice`, `multibroadcast`, and `topk`. The general builder accepts integral, finite floating-point, Boolean, string, null, and array values, including Boolean arrays. It materializes the complete attribute text before calling native code; arbitrary C format placeholders and C variadic values are excluded.

```csharp
var attributes = MIGraphXOperationAttributes.ForReshape(new long[] { 1, 4 });
using var operation = MIGraphXOperation.Create(nativePath, "reshape", attributes);
using var clone = operation.Clone();
```

Use attributes valid for the selected native operation and model shape. Managed serialization does not establish every operation's semantics on every target. See the [`M12TypedOperationAttributes` sample](https://github.com/guojin-yan/MIGraphX-CSharp-API/tree/main/samples/M12TypedOperationAttributes) for all five named forms.

Run it without arguments to inspect the managed serialization path on a development machine without MIGraphX installed:

```powershell
dotnet run --project .\samples\M12TypedOperationAttributes\M12TypedOperationAttributes.csproj -c Release
```

To additionally create and clone the operations, pass one absolute path to the MIGraphX C library on a compatible Linux host:

```powershell
dotnet run --project .\samples\M12TypedOperationAttributes\M12TypedOperationAttributes.csproj -c Release -- `
  /absolute/path/to/libmigraphx_c.so.3
```

The native path requires the frozen MIGraphX/ROCm environment and is not a substitute for independently reviewed runtime evidence. Arbitrary format placeholders, unmanaged pointers, and general C variadic values remain outside the managed API boundary.

中文说明：`M12TypedOperationAttributes` 示例覆盖 reshape、transpose、slice、multibroadcast、topk 五种已审查属性形态。不传参数时只执行托管序列化；传入 MIGraphX C 库绝对路径后才创建并复制 native operation。任意 format placeholder、非托管指针和通用 C 可变参数仍不在托管 API 边界内。
