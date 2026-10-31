# Dynamic shapes, persistence and cache

Use explicit ONNX options to override input dimensions before parsing. Static override dimensions must be positive; an empty dimension list represents a scalar.

```csharp
using var options = new MIGraphXOnnxOptions(nativePath);
options.SetInputParameterShape("input", new long[] { 1, 4 });
using var program = MIGraphXProgram.ParseOnnxFile(modelPath, options);
```

`SetDynamicInputParameterShape` accepts `MIGraphXDynamicDimension` ranges. Dynamic shapes expose `Rank` and `DynamicDimensions`; `Lengths`, `Strides`, `ElementCount` and `ByteCount` throw because there is no single concrete value. Use a concrete static shape before creating a typed host argument. Dedicated real-runtime dynamic-shape behavior still requires application validation.

Synchronous host, asynchronous host and HIP device-input execution validate concrete input types, rank and inclusive dimension bounds before native submission. Any length inside a range is accepted by this check, even when it is absent from `Optimals`. For example, a first-axis range `[2, 4]` with optimal value `3` accepts lengths `2`, `3` and `4`. This validation uses the explicit managed overrides retained when parsing; it does not reconstruct missing ranges from a loaded native program.

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

The key also includes the fixed header/API and managed build identity. The default managed identity contains the loaded core assembly's informational version (including its source commit when supplied during build) and module version ID (MVID). Different compiled builds or target-framework assemblies therefore use separate entries even when their package versions match. This identity is read from the loaded assembly and does not depend on its installation path. Existing entries created with the older version-only default are left in place and no longer selected; the first lookup builds a new entry. An explicit `managedIdentity` replaces the default, so callers supplying one must distinguish incompatible builds themselves.

A versioned JSON sidecar and payload hash detect mismatches or corruption. Entries are atomically replaced through temporary files in the same directory. In-process callers using the same normalized root and key share a writer lock, including directory names with or without a trailing separator. Filesystem links and concurrent writes from separate processes are outside this locking guarantee. `Kind` and `PreviousLookup` distinguish hits, misses, corruption and rebuilds. Metadata and override collections are independent read-only snapshots.

Rebuild entries when model, native fingerprint, package identity, target, options or overrides change. Cache integrity checks do not provide a cross-version serialization guarantee or complete restart qualification.

中文提示：动态形状只表示范围；创建类型化输入前需要具体静态形状。缓存根目录必须显式指定，默认缓存身份包含实际加载程序集的版本、构建提交（构建时提供）和 MVID，同版本的不同构建也会分别缓存。旧版默认缓存不会被删除，但会重新构建新条目；自行指定 managedIdentity 时需自行保证构建隔离。同进程、同规范化目录和 key 的写入会串行处理，符号链接别名和跨进程写入不在该锁定保证内。
