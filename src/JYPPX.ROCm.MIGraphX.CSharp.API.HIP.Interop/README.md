# MIGraphX HIP interop adapter

This optional adapter depends on the matching MIGraphX core version and HipSharp `0.9.1`, while keeping both core packages independent. It exposes `MIGraphXHipExecution`, `MIGraphXHipAsyncRun` and `MIGraphXHipDeviceInput`.

Compile with `offloadCopy=true` for `RunHostAsync`, or `offloadCopy=false` for `RunDeviceAsync` with validated `HipDeviceMemory`. Resources remain leased until stream completion. Outputs are independent host copies after completion.

See [HIP integration](../../docs/guides/hip-interop.md) for submission, synchronization, disposal and runtime limitations. The adapter contains no native runtime and makes no zero-copy or performance guarantee.
