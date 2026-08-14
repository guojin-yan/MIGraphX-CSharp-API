# M1/M2 official runtime validation

The unified official session passed against pushed commit `f1a11cfd1701a041cee29188f7600c85b34ae260`. The checkout was clean and detached at that SHA. The source tree's prior artifacts and smoke results were removed before execution; dependency caches were retained.

## Recorded environment

| Component | Recorded value |
| --- | --- |
| OS / architecture | Ubuntu 24.04 / x86-64, kernel 6.8.0-79-generic |
| Effective cgroup quota | 16 CPU, 64 GiB memory |
| ROCm | 7.2.1 |
| MIGraphX package | `2.15.0.70201-81~24.04` |
| GPU | One AMD Radeon Graphics, gfx1100, 51,522,830,336 bytes VRAM |
| .NET / PowerShell | SDK 10.0.301, runtime 10.0.9 / PowerShell 7.5.3 |
| Toolchain | CMake 3.28.3, GCC/G++ 13.3.0 |

The installed header matched SHA-256 `a3fe22484b07bbfd61572a8b8e6186b05e18341b12f3f27303effc4e820179c2`. The resolved official library had SONAME `libmigraphx_c.so.3`, SHA-256 `3b012a738306e2d4499d0aa0dce7b73f96a96209ade45369ad9194c208801aff`, and no missing direct dependency. All 41 cumulative M2 subset names matched the header-validated managed declarations, fake exports, and official ELF exports.

## Executed results

| Path | Result |
| --- | --- |
| M1 explicit loader and exports | Official library loaded; fixed exports verified |
| M1 strict UTF-8 | A non-ASCII target name reached the runtime intact and was rejected as an unknown target with status 4 |
| M1 valid lifecycle | `gpu` target/program create, assign-to, and destroy executed |
| M2 model | Generated 128-byte ONNX Identity, SHA-256 `0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9` |
| M2 parse and shape | File and buffer paths; one input/output, each `float32[1,4]` |
| M2 compile and run | `gpu` target, offload-copy, synchronous run |
| M2 reference | Input/output `[0.25,-1,2,9]`; exact match |

The complete cloud script exited 0 in 66 seconds after a 12-second prewarm. The raw M2 smoke deliberately reported `runtime-candidate-executed`. After transfer, file-hash verification and an independent JSON review asserted the commit, model hash, both parse paths, shapes, output, and reference match; only then was M2 and GPU inference recorded as `runtime-executed`.

## Limits

This is one environment and one generated Identity model. It does not validate other Linux distributions, ROCm/MIGraphX versions, GPUs, Windows/macOS native runtime, dynamic shapes, multiple inputs/outputs, non-float32 tensors, async/stream APIs, device buffers, runtime packages, or performance.

Earlier SHA-specific failures remain separate evidence: missing Git executable modes, malformed ELF diagnostic classification, and treating an arbitrary UTF-8 probe as a valid target plugin. None of those failed SHAs inherits this successful result.

中文摘要：统一验证在 `f1a11cf...` 的 clean detached checkout 上通过。官方库、M1 生命周期、M2 file/buffer parse、gfx1100 GPU 同步编译/执行与 Identity reference 对比均已真实运行；结论只适用于表中的单一环境和受限路径。
