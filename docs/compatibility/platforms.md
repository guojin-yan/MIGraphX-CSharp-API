# Platforms and evidence levels

| Claim | Status | Meaning |
| --- | --- | --- |
| Managed build | M1/M2/M3 local validation | All 15 exact TFMs compile with the generated dual declaration paths |
| Package assets | M1/M2/M3 local validation | Each TFM has a DLL and XML document in the managed-only candidate package |
| Clean consumer | M1/M2/M3 local validation | Four representative consumers restore and build from the candidate package |
| Frozen header and official ELF | Statically verified | All 159 header functions match the official Linux ELF; 158 have managed EntryPoints and one C-variadic declaration is explicitly unsupported |
| M3 normalized inventory | Statically verified | 159 functions, 2 enums, 25 handles, and 6 callbacks close over 192 mutually exclusive classifications |
| M3 critical ABI patterns | Fake-native executed | Callback lifetime/exception boundary, bool, size_t, UTF-8, borrowed/out pointers, array length, and cleanup ran against a minimal test substitute |
| Lifecycle and restricted ONNX workflow | Fake-native executed | A local C substitute ran loader, parse, shape, compile, run, output-copy, cleanup, and concurrency tests |
| Official M1/M2 runtime | Runtime-executed | Passed at `f1a11cfd1701a041cee29188f7600c85b34ae260` on the single recorded Ubuntu/ROCm/MIGraphX/gfx1100 environment |
| Windows/macOS native runtime | Unverified diagnostic candidates | Candidates are honest loader diagnostics, not an official MIGraphX build/support claim |
| Official ONNX parse/compile/run | Runtime-executed | Generated Identity file and buffer paths compiled and ran synchronously with matching reference output |
| AMD GPU | Runtime-executed | One gfx1100 GPU executed the restricted static float32 Identity graph; this is not a general device claim |
| Runtime NuGet | Disabled | Native closure and licenses are incomplete; runtime packaging fails closed |

The fixed package targets Ubuntu 24.04 amd64 metadata and establishes the Linux SONAME `libmigraphx_c.so.3`. The official record additionally proves one exact host configuration: Ubuntu 24.04 x86-64, ROCm 7.2.1, MIGraphX `2.15.0.70201-81~24.04`, and gfx1100. It does not establish other distributions, versions, devices, models, dynamic shapes, async, or device-buffer paths.

中文摘要：平台表把“可编译”“完整头/官方 ELF 静态对照”“fake-native 执行”和“官方 runtime 执行”分开。M3 新增声明没有官方 runtime 结论；精确环境的 M1/M2 已运行，Windows/macOS 仍不因候选字符串存在而获得支持声明。
