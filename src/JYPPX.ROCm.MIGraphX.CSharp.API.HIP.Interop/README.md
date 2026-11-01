# MIGraphX HIP interop adapter

This optional adapter depends on the matching MIGraphX core version and HipSharp `0.10.0`, while keeping both core packages independent. It exposes `MIGraphXHipExecution`, `MIGraphXHipAsyncRun` and `MIGraphXHipDeviceInput`.

Compile with `offloadCopy=true` for `RunHostAsync`, or `offloadCopy=false` for `RunDeviceAsync` with validated `HipDeviceMemory`. Resources remain leased until stream completion. Outputs are independent host copies after completion.

From 0.0.2, `RunDeviceIntoAsync` writes ordered D2D copies into caller-owned device buffers and creates no host outputs. Enqueue subsequent HIP kernels on the same stream to keep preprocessing, inference and postprocessing on the GPU. This is a device-resident pipeline with D2D output copies, not zero-copy output borrowing.

See [HIP integration](../../docs/guides/hip-interop.md) for submission, synchronization, disposal and runtime limitations. The adapter contains no native runtime and makes no zero-copy or performance guarantee.
