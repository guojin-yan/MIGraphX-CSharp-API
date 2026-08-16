# Turning C API introspection into an explainable .NET contract

A C function that returns `char*` is not yet a .NET capability API. A C function named `equal` is not yet a correct `Equals` implementation. M10 uses five small MIGraphX entry points to show why a managed wrapper must first recover version, lifetime, and semantic boundaries.

## Start from implementation, not the function name

The fixed source is AMDMIGraphX `rocm-7.2.1` commit `de19b73ad280476e646512b847885eda100ec35e`; the generated header hash is `a3fe2248...179c2`. The ONNX registry is a function-static vector copied from a parser map whose names are sorted. The C name accessor uses `vector::at` and returns `c_str()` from that vector. That establishes three useful facts: bounds errors become status failures, the pointer is borrowed, and the managed caller can copy it immediately.

It does not establish an opset matrix. A registered parser name says nothing by itself about attributes, shapes, data types, target lowering, a particular model, or a GPU run. The managed API therefore calls the result a registry snapshot and documents it as a version-bound capability hint.

## Copy first, then expose

`GetRegisteredOperators` reads `size_t` into an overflow-checked managed count, initializes no public pointer state, decodes every name with strict UTF-8, and copies it to `System.String`. It reads the count again before returning. Null pointers, invalid UTF-8, mid-list status failures, and count drift discard the partial array.

The fake-native suite makes each of those failures executable. It also supplies a library with the full earlier M2/M4 surface but no M10 exports, proving in isolated processes that the loader reports the exact missing registry and equality sets rather than claiming the whole ONNX frontend is absent.

## Equality has several meanings

MIGraphX argument equality is inherited from `raw_data::operator==`: two native empty values compare equal; otherwise it compares shape, then logical tensor views for computable types or bytes for non-computable types. The public wrapper creates non-empty owned host copies and does not expose the native empty form. That is useful for exact host-backed content checks, but unsafe as a promise about device memory, cross-runtime values, or numeric tolerance. M10 adopts it only on owned host copies and names it `HasSameNativeContent`.

Program equality is even narrower in the fixed version: `to_string(left) == to_string(right)`. It can detect matching printed program structure, and compile or graph mutation may change the result. It cannot replace a model hash, a cache identity, output comparison, graph-isomorphism analysis, or semantic equivalence. The managed name stays explicit and the limitations sit beside the signature.

Shape equality was rejected. The managed shape is already an immutable owner-free snapshot. Calling the C function would require rebuilding a temporary native shape or retaining a handle and adding disposal. Neither option gives callers a new supported capability, so the map records the fixed native semantics but keeps the entry planned.

## Two handles require one lock order

An equality call must keep both handles alive while Dispose may run on another thread. Locking left then right is not sufficient: a simultaneous right-to-left call can deadlock. M10 assigns every native owner a monotonic id, removes duplicate ids for self-comparison, and acquires locks in ascending order. Tests run both directions concurrently and block inside fake native while another task disposes an operand. Dispose waits; the in-flight comparison completes; later calls fail as disposed.

This internal helper is shared with existing multi-owner compile/run paths, so the M4-M6 resource model stays one model rather than gaining an equality-specific lease type.

## Evidence should remain layered

The unchanged normalized inventory contains 192 entities, 159 C functions, 158 generated managed EntryPoints, and one variadic unsupported function. M10 moves four high-level entries from planned to supported, producing 84/107/1. Its local tests traverse DllImport and LibraryImport through `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0` substitutes.

Those results are `fake-native-executed`, not official MIGraphX execution. The official `346cdd0...` record covers M1/M2 and bounded M9 option acceptance only. Without a newly authorized final-SHA session, M10 remains `runtime-deferred`. That distinction is part of the API design: an explainable wrapper states not only what it does, but exactly what the evidence has and has not established.

中文摘要：C `char*` 入口要先证明 borrowed 生命周期并立即严格 UTF-8 深拷贝；名为 equal 的函数要先追到具体比较实现，再决定是否适合 .NET。M10 采用 registry 与 host argument/program 显式比较，拒绝破坏 shape 快照契约，并用稳定双 owner 锁序证明 Dispose/并发边界。所有新增结果仍只是本地替身证据。
