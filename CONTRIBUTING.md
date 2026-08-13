# Contributing

MIGraphXSharp is evidence-driven. Keep changes scoped, preserve the repository boundary, and run the managed M0 gates before opening a pull request.

## Development rules

- Use `JYPPX.ROCm.MIGraphXSharp` as the top-level namespace.
- Keep the 15-TFM matrix centralized in `Directory.Build.props`.
- Give every public API equivalent Chinese and English XML documentation.
- Generate native declarations only from the fixed manifest/header workflow; do not handwrite files under `Generated`.
- Keep build, package, native-loader, runtime, ONNX, and GPU claims at their actual evidence level.
- Do not add native binaries, private models, credentials, IP addresses, ports, hostnames, device identifiers, or raw cloud logs.

## Local gates

Run `eng/build.ps1`, `eng/test.ps1`, `eng/pack.ps1`, `eng/verify-package.ps1`, and `eng/docs.ps1` in Release configuration. A runtime package request must continue to fail until its complete manifest and validation evidence are reviewed.

The license is pending an owner decision. Do not publish a NuGet package or create a release until licensing is resolved.
