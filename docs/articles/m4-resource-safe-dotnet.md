# Making a native inference API resource-safe in .NET

MIGraphX exposes a compact C ABI, but a direct pointer-shaped projection is not yet a safe .NET API. M4 focuses on the boundary between those layers: who owns each handle, how long borrowed buffers live, what happens when construction fails halfway through, and which facts can be copied into durable managed values.

The central decision is that a shape returned from a native collection is metadata, not a durable object handle. M4 reads its datatype, lengths, strides, element count, byte count, and layout flags while the collection is alive, checks every `size_t` conversion, and returns an immutable `MIGraphXShape`. That snapshot stays useful after the native collection and program are disposed.

Arguments require the opposite treatment. The MIGraphX argument can borrow a host pointer through synchronous execution, so M4 allocates and owns a host buffer, copies a precisely mapped unmanaged array into it, and releases the native argument before freeing the buffer. A parameter map deep-copies arguments again. This costs a copy, but it removes a class of use-after-free bugs when callers mutate arrays or explicitly dispose an input argument before Run.

Native run outputs are borrowed from an owned collection. M4 copies every output shape and buffer before destroying that collection, producing independent arguments in a deterministic read-only list. Partial copies are disposed if a later output fails.

Resource safety also changes concurrency. Without upstream proof that a program is thread-safe, M4 serializes same-instance calls with Dispose. Cross-object calls acquire owner locks in a stable internal order. The promise is intentionally narrow: operations fail closed instead of racing a release; it is not a claim that MIGraphX itself supports arbitrary concurrent use.

The evidence is equally narrow. A local C substitute injects statuses, null handles, null borrowed pointers, duplicate names, count drift, dynamic and nonstandard shapes, datatype mismatches, and cleanup failures. This proves managed control flow and ownership behavior. It does not prove that the new implementation ran on AMD's runtime or a GPU. The previous official Identity result remains useful background, but a new managed layer needs its own authorized runtime session before receiving a `runtime-executed` label.

中文提要：安全封装不只是把 `IntPtr` 换成类。它要求把 borrowed shape 立即复制、让 argument 真正拥有 host buffer、在 map 中继续保持独立所有权、在输出集合销毁前复制结果，并让并发 Dispose fail closed。M4 用 fake-native 证明这些托管语义，但不借用旧 GPU 结果夸大新实现的证据等级。
