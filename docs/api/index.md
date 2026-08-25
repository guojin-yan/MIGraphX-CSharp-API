# API reference

M1 exposes diagnostics through `MIGraphXEnvironment`, `MIGraphXEnvironmentReport`, `MIGraphXNativeDiagnostic`, `MIGraphXStatus`, and `MIGraphXException`. M2 adds `MIGraphXOnnxWorkflow`, `MIGraphXOnnxExecutionResult`, and structured `MIGraphXNativeLoadException` diagnostics.

`Probe` accepts only an absolute caller path. `RunFile` accepts absolute native/model paths, while `RunBuffer` accepts an absolute native path and non-empty ONNX bytes. Those M1/M2 signatures and restrictions remain unchanged.

M4 adds `MIGraphXProgram`, immutable `MIGraphXShape`, `MIGraphXArgument`, `MIGraphXTarget`, `MIGraphXOnnxOptions`, `MIGraphXCompileOptions`, `MIGraphXParameterMap`, `MIGraphXArgumentCollection`, and `MIGraphXShapeDataType`. M5 adds dynamic dimensions and ONNX shape overrides, fixed-version `msgpack` program save/load, and an explicit-root cache with deterministic metadata and corruption recovery.

The optional M6 adapter adds `MIGraphXHipAsyncRun`, `MIGraphXHipDeviceInput`, and `MIGraphXHipExecution`. Host and device-input submissions use `RunHostAsync` and `RunDeviceAsync`; completion is explicit through `TryComplete` or `Synchronize`. The adapter exposes no raw pointer, SafeHandle, generated delegate, backend-name string, or internal HIP API type.

M10 adds `MIGraphXOnnxWorkflow.GetRegisteredOperators`, `MIGraphXArgument.HasSameNativeContent`, and `MIGraphXProgram.HasSameNativeContent`. Registry names are strict-UTF-8 managed copies and only indicate parsers registered by the loaded native version. Argument comparison is exact and host-backed; program comparison is the fixed version's printed structural comparison. Neither method defines general .NET equality, hashing, operators, model compatibility, or inference equivalence. `MIGraphXShape` remains an owner-free snapshot and does not project native equality.

The M12 local batch adds graph/module/instruction views, a restricted no-attribute operation factory/clone, TensorFlow options and parsing, quantization option objects, experimental context access, and custom-op callback registration. These wrappers preserve program leases and copied snapshot boundaries. Local fake-native compilation and focused tests pass; real runtime validation remains deferred, and the wrappers are not included in the historical compatibility counts. General C-varargs operation attributes remain outside the managed boundary.

Every native resource accepts an explicit absolute library path and has deterministic Dispose semantics. Shape and collection results are copied snapshots. Dynamic shapes expose ranges rather than fabricated lengths or strides. Typed argument methods require exact unmanaged scalar mappings and never expose borrowed arrays or pointers.

Generated API pages are built from the core and adapter assemblies and their bilingual XML documentation.
