# API compatibility and versioning

The package/project/assembly name is `JYPPX.ROCm.MIGraphX.CSharp.API`; the core C# namespace is `JYPPX.ROCm.MIGraphXSharp`. The optional adapter is `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop`.

Packages are currently unpublished and the repository default is `0.0.0`. During `0.x.x` development, intentional public API changes require review of the compatibility snapshots. Do not treat an engineering candidate as a stable release.

The managed package version is independent of the installed ROCm/MIGraphX version. Updating the managed package does not update AMD system packages. Changing native versions requires a separate compatibility check even when the SONAME is unchanged.

Core and adapter expose the same public contract across all [15 target frameworks](../compatibility/frameworks.md). The adapter references the matching exact core package and exact HipSharp `0.9.1` dependency. Keep these dependencies coherent.

Program serialization and model cache entries are version- and provider-bound. Rebuild cache data when the native fingerprint, managed build identity, target, compile options, model or input overrides change.

中文提示：托管版本与系统 ROCm/MIGraphX 版本独立；开发期公开接口可能变化。升级时检查依赖版本并重建不兼容的缓存。
