# Dynamic shapes, persistence and cache

Use explicit ONNX options to override input dimensions before parsing. Static override dimensions must be positive; an empty dimension list represents a scalar.

```csharp
using var options = new MIGraphXOnnxOptions(nativePath);
options.SetInputParameterShape("input", new long[] { 1, 4 });
using var program = MIGraphXProgram.ParseOnnxFile(modelPath, options);
```

`SetDynamicInputParameterShape` accepts `MIGraphXDynamicDimension` ranges. Dynamic shapes expose `Rank` and `DynamicDimensions`; `Lengths`, `Strides`, `ElementCount` and `ByteCount` throw because there is no single concrete value. Use a concrete static shape before creating a typed host argument. Dedicated real-runtime dynamic-shape behavior still requires application validation.

## Save and load

```csharp
using var fileOptions = new MIGraphXFileOptions(nativePath, "msgpack");
program.Save("/absolute/path/to/program.msgpack", fileOptions);
using var restored = MIGraphXProgram.Load(
    "/absolute/path/to/program.msgpack", fileOptions);
```

Only the fixed `msgpack` format is accepted. Paths must be absolute. Serialized programs are bound to the native version and compilation context; do not assume portability across native versions or targets.

## Model cache

`MIGraphXModelCache` requires an explicit absolute cache directory. Construct `MIGraphXCacheMetadata` from the model SHA-256, native fingerprint, target, compile options, format and ordered input overrides, then call `GetOrBuild(metadata, fileOptions, builder)` with a function that returns an owned program. Dispose the returned `MIGraphXCacheResult`, which owns its `Program`.

The key also includes the fixed header/API and managed build identity. A versioned JSON sidecar and payload hash detect mismatches or corruption. Entries are atomically replaced through temporary files in the same directory. `Kind` and `PreviousLookup` distinguish hits, misses, corruption and rebuilds. Metadata and override collections are independent read-only snapshots.

Rebuild entries when model, native fingerprint, package identity, target, options or overrides change. Cache integrity checks do not provide a cross-version serialization guarantee or complete restart qualification.

中文提示：动态形状只表示范围；创建类型化输入前需要具体静态形状。缓存根目录必须显式指定，缓存与模型、运行库、托管版本、目标和选项绑定，不能跨版本直接复用。
