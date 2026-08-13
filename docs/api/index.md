# API reference

M1 exposes diagnostics through `MIGraphXEnvironment`, `MIGraphXEnvironmentReport`, `MIGraphXNativeDiagnostic`, `MIGraphXStatus`, and `MIGraphXException`. M2 adds `MIGraphXOnnxWorkflow`, `MIGraphXOnnxExecutionResult`, and structured `MIGraphXNativeLoadException` diagnostics.

`Probe` accepts only an absolute caller path. `RunFile` accepts absolute native/model paths, while `RunBuffer` accepts an absolute native path and non-empty ONNX bytes. The M2 public surface intentionally stays a restricted synchronous workflow rather than exposing the deferred general Program/Shape/Argument design.

Generated API pages are built from the core assembly and its bilingual XML documentation.
