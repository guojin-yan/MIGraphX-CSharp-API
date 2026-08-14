# M5 dynamic shape and cache design

M5 extends the M4 resource-safe synchronous layer with two independent capabilities. `MIGraphXDynamicDimension` is an immutable managed value: minimum and maximum are a checked non-negative `long`, and optimal values are copied, in-range, strictly increasing, and non-duplicated. A dynamic `MIGraphXShape` exposes only rank and `DynamicDimensions`; concrete lengths, strides, element count, and byte count throw because they are not defined until a shape is selected. Typed host arguments remain static-only.

`MIGraphXOnnxOptions` validates names with the existing strict UTF-8 policy. Static and dynamic input overrides replace an earlier override for the same name; the managed copy is retained so a parsed dynamic parameter snapshot can report the supplied ranges. The C API exposes no dimension getters, so a native dynamic shape without an explicit managed override remains rejected rather than inventing a range.

The native ownership sequence is: copy managed values to checked `size_t` storage; create temporary optimals; create temporary dimensions; create an owned dynamic-dimension collection; call the consuming API; then release collection, dimensions, optimals, and unmanaged arrays in reverse order. Collection elements returned by `migraphx_dynamic_dimensions_get` are borrowed and copied while the collection is alive.

`MIGraphXFileOptions` owns one immutable native options handle and currently accepts only `msgpack`, the format exercised by the fake-native path for the pinned API. `MIGraphXProgram.Save` borrows the program and options; `MIGraphXProgram.Load` returns a new owned program and conservatively marks it uncompiled.

## Cache envelope

`MIGraphXModelCache` never selects a process-global directory. The caller supplies an absolute root. The key is the SHA-256 of canonical metadata containing:

- schema version, source model SHA-256, fixed header SHA-256, API identity, managed package/build identity;
- native library file fingerprint, target name, compile options, and file format;
- input overrides sorted by ordinal input name, with dimensions in caller order; `dynamicDimensions` is present even for an empty-rank dynamic override, so it cannot collide with an empty-rank static override.

The payload is `<key>.migraphx`; the sidecar is `<key>.json` and follows `compatibility/schemas/m5-cache.schema.json`. The sidecar includes the payload SHA-256. Writes create both files beside their final names, then atomically replace each final file. A missing sidecar, missing payload, metadata mismatch, payload hash mismatch, truncated file, or native load failure is a closed cache miss and is rebuilt. In-process writes for one root/key are serialized. `MIGraphXCacheResult.PreviousLookup` distinguishes cold miss from corruption when a rebuild succeeds.

No model bytes, cache payloads, native binaries, absolute paths, usernames, or credentials are part of the package or repository. The cache is valid only for the pinned native/API identity and the exact metadata used to produce it.

中文摘要：M5 使用托管不可变动态维度表达范围，动态 Shape 不伪造 concrete 元数据；ONNX override 保留托管范围并严格校验 UTF-8 名称。FileOptions 只承诺已测试的 `msgpack`，Load 后要求重新编译。缓存由显式根目录、规范化 metadata、payload hash 和原子写组成，不跨版本、target 或 native fingerprint 通用。
