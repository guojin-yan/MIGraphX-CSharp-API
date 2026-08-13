# Platforms and evidence levels

| Claim | Status | Meaning |
| --- | --- | --- |
| Managed build | M1 local validation | All 15 exact TFMs compile with the generated dual declaration paths |
| Package assets | M1 local validation | Each TFM has a DLL and XML document in the managed-only candidate package |
| Clean consumer | M1 local validation | Four representative consumers restore and build from the candidate package |
| Frozen header and official ELF subset | Statically verified | Header SHA, ABI declarations, and all six names match the official Linux ELF export table |
| Loader and target/program lifetime | Fake-native executed | A local C substitute ran loader, export, UTF-8, status, SafeHandle, assign, and concurrency tests |
| Official Linux loader and target/program lifetime | Planned | Requires an authorized Radeon Cloud session and frozen installation evidence |
| Windows/macOS native runtime | Unverified diagnostic candidates | Candidates are honest loader diagnostics, not an official MIGraphX build/support claim |
| ONNX frontend / parse | Not applicable to M1 | M2 scope; header/export facts are not parse execution evidence |
| AMD GPU | Not applicable to M1 | M1 does not compile or run a graph |
| Runtime NuGet | Disabled | Native closure and licenses are incomplete; runtime packaging fails closed |

The fixed package targets Ubuntu 24.04 amd64 metadata and establishes the Linux SONAME `libmigraphx_c.so.3`. This static fact does not establish that it loads on any particular host. Only an official runtime record may support a corresponding native capability claim.

中文摘要：平台表把“可编译”“官方 ELF 静态对照”“fake-native 执行”和“官方 runtime 执行”分开。Windows/macOS 不因候选字符串存在而获得支持声明。
