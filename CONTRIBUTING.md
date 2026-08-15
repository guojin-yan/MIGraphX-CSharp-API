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

Run `eng/generate-interop.ps1 -AcquireHeader -Verify`, all M1-M3 ABI gates, `eng/test.ps1`, core/adapter pack and package audits, `eng/docs.ps1`, and `eng/verify-release-candidate.ps1` in Release configuration. Public API snapshots must match all 15 TFMs. A Runtime NuGet request must continue to fail because the permanent deployment policy is system-native.

The project is licensed under Apache-2.0. Unless explicitly stated otherwise, intentionally submitted contributions are provided under that license. The repository default is `0.0.0`; `0.9.0-rc.1` is an unpublished local prerelease candidate. Keep development on the `0.x.x` line. Do not publish a NuGet package, bump final `1.0.0`, tag, deploy Pages, or create a release without explicit owner authorization. Final `1.0.0` additionally requires completed Windows runtime validation.
