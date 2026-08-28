# M12 typed operation attributes sample

The sample materializes the five reviewed operation attribute forms without passing arbitrary C format placeholders or general C variadic values through the native ABI: `reshape`, `transpose`, `slice`, `multibroadcast`, and `topk`. With no arguments it runs the managed serialization path, which is useful on Windows without MIGraphX installed.

To additionally create and clone the operations, pass one absolute path to the MIGraphX C library on a compatible Linux host:

```powershell
dotnet run --project .\samples\M12TypedOperationAttributes\M12TypedOperationAttributes.csproj -c Release -- `
  /absolute/path/to/libmigraphx_c.so.3
```

The native path requires the frozen MIGraphX/ROCm environment and is not a substitute for the independently reviewed runtime evidence.

中文说明：不传参数时仅运行托管属性物化；传入 MIGraphX C 库绝对路径时会创建并复制五种已审查 operation。任意 C format placeholder 和通用 C 可变参数仍不在托管边界内。
