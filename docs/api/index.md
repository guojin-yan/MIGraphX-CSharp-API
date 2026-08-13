# API reference

M1 exposes a minimal diagnostic API: `MIGraphXEnvironment`, `MIGraphXEnvironmentReport`, `MIGraphXNativeDiagnostic`, `MIGraphXStatus`, and `MIGraphXException`. `MIGraphXBuildInfo` reports the managed package baseline.

`Probe` accepts only an absolute caller path. `ProbeSystem` records the audited candidate order. Both report loading/export/object-lifecycle facts without claiming ONNX or GPU capability. Internal SafeHandles own the M1 target/program handles and are intentionally not the deferred public Program/Shape/Argument design.

Generated API pages are built from the core assembly and its bilingual XML documentation.
