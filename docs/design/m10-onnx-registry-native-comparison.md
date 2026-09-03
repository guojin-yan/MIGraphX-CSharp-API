# M10 ONNX registry and native content comparison

M10 projects four of five reviewed C entry points. `migraphx_get_onnx_operators_size` and `migraphx_get_onnx_operator_name_at_index` become one copied registry snapshot API. `migraphx_argument_equal` and `migraphx_program_equal` become explicitly named, version-bound content comparisons. `migraphx_shape_equal` remains planned. The aggregate high-level map is 84 supported, 107 planned, and one unsupported item over the unchanged 192-item inventory.

## Fixed implementation evidence

The source identity is AMDMIGraphX `rocm-7.2.1`, commit `de19b73ad280476e646512b847885eda100ec35e`. The frozen generated header has SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`. All five declarations have generated `DllImport` and `LibraryImport` forms and are present in the hash-reviewed official ELF; this is static evidence, not M10 runtime execution.

| Candidate | Fixed implementation | M10 decision |
| --- | --- | --- |
| ONNX registry size | returns the size of a function-static vector initialized from registered parser names | adopted |
| ONNX registry name | uses bounds-checked `vector::at(index)` and returns a borrowed `c_str()` pointer | adopted |
| shape equality | compares type, static lengths/strides or dynamic ranges, and tuple sub-shapes | retained planned |
| argument equality | treats two native empty values as equal; otherwise compares complete shape, then logical computable tensor views or non-computable byte views | adopted for owned host-backed arguments |
| program equality | compares `to_string(program)` output | adopted as printed structural comparison |

The ONNX parser map is copied into a sorted vector during initialization. Managed code preserves the native index order it receives and does not sort, deduplicate, or normalize case. The API does not promise that ordering across MIGraphX releases.

## Registry snapshot contract

`MIGraphXOnnxWorkflow.GetRegisteredOperators` requires an explicit absolute native-library path and the fixed M2 plus M10 registry exports. It reads a checked `size_t`, copies each borrowed string immediately with strict UTF-8, rejects an empty name, and returns an `Array.AsReadOnly` snapshot. A null name, invalid UTF-8, native status failure, count above `Int32.MaxValue`, unterminated string, index failure, count drift, or second native root fails closed. A partial result is never returned.

The result is a parser-registry capability hint for one loaded native version. It is not an ONNX opset matrix and does not prove that a model, target, device, data type, shape, or operator configuration can parse, compile, or run.

## Explicit comparison contract

`MIGraphXArgument.HasSameNativeContent` accepts only arguments created by the same loaded native root and backed by owned host copies. The fixed `raw_data` comparison has a both-empty special case, but the public managed argument paths create non-empty host-backed values and do not expose that native empty form. Native argument comparison may dereference data through a host tensor view, so internally borrowed device buffers are rejected. Equality is exact: there is no numeric tolerance, cross-device comparison, storage portability, or semantic tensor claim.

`MIGraphXProgram.HasSameNativeContent` exposes the fixed implementation's printed structural comparison. Parse, compile, sort, or graph mutation can change printed content. The result is not a model-file hash, graph-isomorphism proof, inference-semantic equivalence, output comparison, cache key, or compiled-binary identity.

Both methods lock their two `NativeResourceOwner` instances by monotonically assigned owner id. Same-object calls deduplicate the lock; reverse concurrent calls acquire the same total order. The comparison keeps both handles alive, transfers no ownership, and serializes against Dispose. A call that begins after either owner is disposed throws `ObjectDisposedException`.

M10 deliberately does not override `object.Equals`, `GetHashCode`, `==`, or `!=`, and does not implement `IEquatable<T>`. Those broader .NET contracts would require stable equivalence and hashing semantics across native versions that the fixed C API does not provide.

## Rejected shape projection

`MIGraphXShape` is an immutable managed snapshot with no native owner or disposal contract. The supported static and dynamic fields already provide the value needed by callers. Invoking native equality would require temporary native materialization or a retained handle, add failure and lifetime behavior, and provide no new supported information. Shape equality therefore remains `planned` and only `statically-verified`; fake-native does not export or execute `migraphx_shape_equal` for M10.

## Evidence boundary

Local tests execute success, difference, status failure, invalid C bool, reverse concurrency, and Dispose races for adopted comparisons. Registry tests execute non-empty/empty data, ASCII/non-ASCII names, empty-name rejection, bounds, overflow, null pointer, invalid UTF-8, mid-copy failure, count drift, second-root rejection, and exact missing-export diagnostics. Representative `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0` processes traverse the generated DllImport/LibraryImport paths.

No new official host authorization was available. M10 is therefore `runtime-deferred`; its adopted mappings remain `fake-native-executed`, and the official M1/M2 plus bounded M9 record at `346cdd0...` is not inherited.

中文摘要：M10 将 ONNX parser registry 深拷贝为只读托管快照，并采用 argument/program 的显式、版本绑定原生内容比较；shape equality 因会破坏无 owner 的快照契约而保留 planned。双 owner 比较按稳定 id 顺序加锁并与 Dispose 串行；本阶段只有本地 fake-native 证据，没有新的官方 runtime 结论。
