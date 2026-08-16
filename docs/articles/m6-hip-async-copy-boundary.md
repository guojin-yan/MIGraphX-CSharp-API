# MIGraphXSharp + HipSharp: can device buffers and async streams really reduce copies?

The honest M6 answer is not yet. The new adapter can submit MIGraphX work to an owned HipSharp stream and can borrow a `HipDeviceMemory` input safely, but the evidence does not support a zero-copy or performance claim.

The important result is a lifecycle contract. MIGraphX's fixed `rocm-7.2.1` C ABI consumes a HIP stream pointer together with the underlying `ihipStream_t` name. Its asynchronous execution environment orders work against that stream. The managed adapter therefore treats the native return as enqueue success, not output readiness. `TryComplete` uses `HipStream.Query`; `Synchronize` and unfinished result disposal use `HipStream.Synchronize`.

That creates a difficult ownership interval. The caller may dispose the program, parameter map, input argument, device allocation, or result immediately after enqueue. None of those actions may invalidate memory still reachable by native work. M6 holds SafeHandle references for the MIGraphX program/map/arguments, keeps the native output collection alive, and registers one HipSharp pending callback under the stream lock. Device memory uses HipSharp's existing pointer reference count, so early disposal is deferred until completion.

The host path requires `offloadCopy=true`. Parameter-map values are already owned host copies, and completion copies each host-readable output into a new owned argument. The device path requires `offloadCopy=false`. It preserves the input device pointer through the adapter and MIGraphX argument creation, but output materialization performs an explicit synchronous D2H copy after the stream completes. The public result therefore has a uniform owned-host-output contract.

This choice is conservative for a reason. Returning a borrowed device output would extend stream, program, native collection, and device-memory leases into every consumer operation. Reading a device address with `Marshal.Copy` would simply be incorrect. An explicit D2H boundary is slower than an imagined zero-copy result, but it is testable and does not expose raw handles.

The local substitutes prove that outputs stay unavailable while queued, NotReady does not release leases, distinct streams complete independently, caller disposal is deferred, and D2H failures become terminal result errors only after cleanup. They also prove that the adapter package exposes only three types and depends on the two core packages without merging them.

What they do not prove matters more: the substitute is not AMD MIGraphX, its timing is not GPU timing, and pointer identity alone does not establish that the runtime avoided internal copies. A future authorized runtime session must compare synchronous host, asynchronous host, and device-input paths while recording H2D, enqueue, wait, and D2H separately. Until then, the title remains a question and M6 remains `statically-verified` plus `fake-native-executed`.

中文摘要：M6 解决的是可证明的生命周期与拷贝边界，而不是性能宣传。device input 指针受租约保护，但输出仍在完成后显式 D2H；没有官方 runtime 实测前，不得称为 zero-copy 或更快。
