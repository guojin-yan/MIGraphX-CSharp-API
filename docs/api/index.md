# API reference

M1 exposes diagnostics through `MIGraphXEnvironment`, `MIGraphXEnvironmentReport`, `MIGraphXNativeDiagnostic`, `MIGraphXStatus`, and `MIGraphXException`. M2 adds `MIGraphXOnnxWorkflow`, `MIGraphXOnnxExecutionResult`, and structured `MIGraphXNativeLoadException` diagnostics.

`Probe` accepts only an absolute caller path. `RunFile` accepts absolute native/model paths, while `RunBuffer` accepts an absolute native path and non-empty ONNX bytes. Those M1/M2 signatures and restrictions remain unchanged.

M4 adds `MIGraphXProgram`, immutable `MIGraphXShape`, `MIGraphXArgument`, `MIGraphXTarget`, `MIGraphXOnnxOptions`, `MIGraphXCompileOptions`, `MIGraphXParameterMap`, `MIGraphXArgumentCollection`, and `MIGraphXShapeDataType`. Raw pointers, generated native enums/delegates, dynamic shapes, async, streams, and device buffers remain internal or planned.

Every native resource accepts an explicit absolute library path and has deterministic Dispose semantics. Shape and collection results are copied snapshots. Typed argument methods require exact unmanaged scalar mappings and never expose borrowed arrays or pointers.

Generated API pages are built from the core assembly and its bilingual XML documentation.
