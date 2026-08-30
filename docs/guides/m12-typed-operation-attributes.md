# M12 typed operation attributes

The [`M12TypedOperationAttributes` sample](https://github.com/guojin-yan/MIGraphX-CSharp-API/tree/main/samples/M12TypedOperationAttributes) materializes the five reviewed operation forms without passing arbitrary C format placeholders or general C variadic values through the native ABI: `reshape`, `transpose`, `slice`, `multibroadcast`, and `topk`.

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
