# API compatibility and versioning

The package/project/assembly name is `JYPPX.ROCm.MIGraphX.CSharp.API`; the core C# namespace is `JYPPX.ROCm.MIGraphXSharp`. The optional adapter is `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop`.

The stable package line and repository default are `1.0.0`. Public API changes require review of the compatibility snapshots; breaking changes to stable APIs require a new major version. Experimental APIs retain the limitations documented in the platform guide.

The managed package version is independent of the installed ROCm/MIGraphX version. Updating the managed package does not update AMD system packages. Changing native versions requires a separate compatibility check even when the SONAME is unchanged.

Core and adapter expose the same public contract across all [15 target frameworks](../compatibility/frameworks.md). The adapter references the matching exact core package and exact HipSharp `1.0.0` dependency. Keep these dependencies coherent.

Program serialization and model cache entries are version- and provider-bound. Rebuild cache data when the native fingerprint, managed build identity, target, compile options, model or input overrides change.

中文提示：托管版本与系统 ROCm/MIGraphX 版本独立；稳定 API 的破坏性修改需要升级主版本。升级时检查依赖版本并重建不兼容的缓存。
