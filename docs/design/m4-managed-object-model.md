# M4 managed object and ownership design

M4 adds a deliberately small synchronous object layer over the frozen ROCm 7.2.1 / MIGraphX C API. It does not expose the internal generated declarations. The complete low-to-high mapping is `compatibility/m4-high-level-api-map.json`: all 192 M3 inventory items close as 52 supported, 139 planned, and one unsupported. The C-variadic `migraphx_operation_create` remains unsupported.

## Public boundary

`MIGraphXProgram`, `MIGraphXTarget`, `MIGraphXOnnxOptions`, `MIGraphXCompileOptions`, `MIGraphXArgument`, and `MIGraphXParameterMap` own native handles. `MIGraphXArgumentCollection` owns detached output arguments. `MIGraphXShape` is an immutable metadata snapshot and deliberately owns no native handle.

The public scalar mapping is limited to `float`, `double`, and signed/unsigned 8-, 16-, 32-, and 64-bit integers. Tuple, bool, half, bfloat16, float8, and packed float4x2 native datatypes remain outside M4. Dynamic ranges are modeled by the M5 `MIGraphXDynamicDimension` value layer; typed arguments still require a concrete static shape. The mapping rejects unknown enum values rather than casting them through.

ONNX options were default-only in M4. M5 adds explicit static/dynamic input overrides and default dimension setters. External data options, tuning switches, async, streams, device buffers, and graph editing remain planned; Save/Load is limited to the fixed-version M5 file-options path.

## Ownership table

| Public type | Native ownership | Borrowed data | Dispose and failure behavior |
| --- | --- | --- | --- |
| `MIGraphXShape` | None | Native lengths/strides are copied while their collection lives | Immutable snapshot remains valid after every native owner is disposed |
| `MIGraphXTarget` | Owned target SafeHandle | Name is borrowed only during create | Repeated Dispose is safe; failed non-null create handles are released |
| `MIGraphXProgram` | Owned program SafeHandle | Target/options/map are borrowed under ordered owner locks | Same-instance calls serialize with Dispose and fail closed afterward |
| `MIGraphXOnnxOptions` | Owned options SafeHandle | Borrowed only during parse | Failed construction cleans non-null handles |
| `MIGraphXCompileOptions` | Owned options SafeHandle | Borrowed only during compile | Setter failure cleans the newly created handle |
| `MIGraphXArgument` | Owned argument SafeHandle and owned unmanaged host buffer | Native argument borrows the object-owned buffer | Argument is destroyed before its buffer; partial construction unwinds in reverse order |
| `MIGraphXParameterMap` | Owned native map and deep-copied argument owners | Program borrows the map during synchronous run | Native map is destroyed before argument copies; failed Add destroys its copy |
| `MIGraphXArgumentCollection` | No retained native collection; owns detached arguments | Run arguments and buffers are copied before native collection destroy | Partial output copies are cleaned on failure; Dispose releases outputs in index order |

The machine-readable equivalent is `compatibility/m4-public-ownership.json`.

## Shape and typed-buffer validation

Static dimensions must be positive; an empty dimension list represents a scalar. Standard strides, element count, and byte count use checked `Int64` arithmetic. Every native `size_t` count or value is checked before conversion to `Int32` or `Int64`. Native length/stride rank mismatch, element/byte inconsistency, collection size drift, null borrowed pointers, duplicate parameter names, and unsupported datatypes fail explicitly.

`MIGraphXArgument.Create<T>` accepts only an exact mapped unmanaged scalar type and an exact element count. Input bytes are copied into object-owned unmanaged memory. No array address, `Span<T>`, or borrowed pointer escapes. Parameter-map Add deep-copies the argument again so explicitly disposing the caller's argument cannot invalidate a later run.

Run results are enumerated in native index order. Each borrowed output shape and buffer is copied while the native arguments collection is alive; the returned collection therefore has no lease on that collection.

## Concurrency and errors

There is no upstream evidence for concurrent operations on one MIGraphX object. M4 serializes operations and Dispose per owner. Operations involving multiple resources acquire locks by a monotonically assigned internal ID, preventing lock-order inversion. Disposed resources fail closed with `ObjectDisposedException`; immutable snapshots remain readable.

Native status failures remain `MIGraphXException` and preserve the C EntryPoint in `Operation`, the raw integer, and known/unknown status mapping. SafeHandle release paths always free native ownership and do not replace an active primary exception with a destroy status. Direct resource methods do not catch or reclassify native call failures.

## Evidence boundary

M4 object construction, snapshots, typed buffers, synchronous run, multi-parameter map validation, multi-output copying, malformed collections, targeted status injection, null outputs, and cleanup execute against the local fake-native substitute. This is `fake-native-executed`, not official runtime evidence.

The official M1/M2 records were revalidated at `346cdd0b01a7f8039f5deb93058928403fccc7dd` on one Ubuntu 24.04 x86-64 ROCm 7.2.1 installation, gfx1100, and the static float32 Identity workflow. The same session's separate M9 option smoke does not prove the broader M4 implementation.

中文摘要：M4 公开小型同步资源对象层。Shape 是不可变复制快照；其余资源通过 owned SafeHandle 和对象自有 host buffer 管理。动态 shape、未映射 dtype、异步、设备 buffer、缓存和图编辑仍不公开。M4 证据仅为本地 fake-native，旧四条官方 runtime 记录不变。
