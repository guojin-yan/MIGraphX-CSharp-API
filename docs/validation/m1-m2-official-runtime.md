# M1/M2 official runtime validation

The latest unified official session revalidated M1/M2 against pushed commit `c435cd0fa1f87496dbb8612e14654b6665eb5ee7` while completing the M9 option smoke and M12 candidate gate. The checkout was clean and detached at that SHA, and the standard script rebuilt the managed gates before official execution. The complete redacted record is maintained at `Radeon_Cloud/records/20260910-c435cd0-m12-runtime/`; the earlier `346cdd0...` session remains historical evidence.

## Recorded environment

| Component | Recorded value |
| --- | --- |
| OS / architecture | Ubuntu 24.04 / x86-64 (kernel version not captured in this session) |
| Effective cgroup quota | 16 CPU, 55 GiB memory |
| ROCm | 7.2.1 |
| MIGraphX package | `2.15.0.70201-81~24.04` |
| GPU | One AMD Radeon Graphics, gfx1100 (VRAM size not captured in this session) |
| .NET / PowerShell | SDK 10.0.112 / PowerShell 7.6.6 |
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

The complete cloud script exited 0. The raw M2 smoke deliberately reported `runtime-candidate-executed`; the M9 path reported `runtime-options-candidate-executed-review-required`. The same session completed the M12 package-only candidate and independent review, and a separate official provider callback probe observed one managed `computeShape` invocation followed by controlled status `4` rejection. These results remain scoped by their evidence labels: M1 is runtime-executed, M2/M9 are candidate review-required, and the callback record is diagnostic-only with no promotion.

## Limits

This is one environment and one generated Identity model. It does not validate other Linux distributions, ROCm/MIGraphX versions, GPUs, Windows/macOS native runtime, dynamic shapes, multiple inputs/outputs, non-float32 tensors, async/stream APIs, device buffers, runtime packages, or performance.

Earlier SHA-specific failures remain separate evidence, including the three M9-session gates for GCC formatting, generated line endings, and the ABI allowlist. None of those failed SHAs inherits this successful result.

中文摘要：统一验证在 `c435cd0...` 的 clean detached checkout 上退出码为 0。官方库、M1 生命周期、M2 file/buffer parse、gfx1100 GPU 同步编译/执行与 Identity reference 对比均已真实运行；另有 M9 候选、M12 全候选及官方 provider callback 受控拒绝记录。M2/M9 仍为 review-required，callback 仅为诊断证据，不产生新的 promotion；结论只适用于表中的单一环境和受限路径。
