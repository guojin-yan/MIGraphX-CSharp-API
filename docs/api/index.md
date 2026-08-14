# API reference

M1 exposes diagnostics through `MIGraphXEnvironment`, `MIGraphXEnvironmentReport`, `MIGraphXNativeDiagnostic`, `MIGraphXStatus`, and `MIGraphXException`. M2 adds `MIGraphXOnnxWorkflow`, `MIGraphXOnnxExecutionResult`, and structured `MIGraphXNativeLoadException` diagnostics.

`Probe` accepts only an absolute caller path. `RunFile` accepts absolute native/model paths, while `RunBuffer` accepts an absolute native path and non-empty ONNX bytes. Those M1/M2 signatures and restrictions remain unchanged.

M4 adds `MIGraphXProgram`, immutable `MIGraphXShape`, `MIGraphXArgument`, `MIGraphXTarget`, `MIGraphXOnnxOptions`, `MIGraphXCompileOptions`, `MIGraphXParameterMap`, `MIGraphXArgumentCollection`, and `MIGraphXShapeDataType`. M5 adds dynamic dimensions and ONNX shape overrides, fixed-version `msgpack` program save/load, and an explicit-root cache with deterministic metadata and corruption recovery.

The optional M6 adapter adds `MIGraphXHipAsyncRun`, `MIGraphXHipDeviceInput`, and `MIGraphXHipExecution`. Host and device-input submissions use `RunHostAsync` and `RunDeviceAsync`; completion is explicit through `TryComplete` or `Synchronize`. The adapter exposes no raw pointer, SafeHandle, generated delegate, backend-name string, or internal HIP API type.

Every native resource accepts an explicit absolute library path and has deterministic Dispose semantics. Shape and collection results are copied snapshots. Dynamic shapes expose ranges rather than fabricated lengths or strides. Typed argument methods require exact unmanaged scalar mappings and never expose borrowed arrays or pointers.

Generated API pages are built from the core and adapter assemblies and their bilingual XML documentation.
