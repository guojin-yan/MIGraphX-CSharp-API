# M12 real-runtime validation plan

M12 local development is complete and its local evidence is closed. Real MIGraphX promotion remains `runtime-deferred`; an earlier source/package-bound candidate probe is recorded separately, but it did not promote any M12 case. This document and `compatibility/m12-runtime-cases.json` define the next authorized execution boundary for the current source.

## Current status

The local Windows environment has no official MIGraphX provider. The fake MIGraphX and fake HIP fixtures, all 15 managed target frameworks, the six M12 focused tests, representative M12 graph execution through both interop families, the 42-test unit suite, the 24 ProjectQuality tests, the package tests, and the historical M11/API gates pass locally. That evidence proves managed ownership and ABI-shape contracts only; it does not prove behavior against MIGraphX 2.15.0.

The M12 matrix is intentionally separate from the historical M3-M11 maps. Its cases remain `officialEvidence: runtime-deferred`, its authorization flags are false, and its review policy requires the candidate label `runtime-candidate-executed-review-required`. No M12 case may be promoted directly by the runner or by a local test.

## Required execution environment

An authorized run must bind all of the following before execution begins:

- Linux with the fixed MIGraphX 2.15.0 and ROCm 7.2.1 provider, plus the exact native header/library identity.
- A clean detached checkout of the final 40-character source SHA and exact managed package identities.
- A new record directory containing copied scripts, fixture/model hashes, package hashes, native dependency closure, process map, and UTC command metadata.
- A reviewed TensorFlow fixture and calibration map. The two ONNX identity fixtures already inherit the frozen M11 SHA-256 values; planned M12 artifacts must receive hashes before use.
- A separately authorized observation policy for GPU/resource checks. The M12 runner must not modify the installed provider, reboot the host, or use an unreviewed alternate native root.

The fixed Windows policy remains unchanged: HIP SDK availability is not treated as MIGraphX availability, and no Windows native M12 session is designed.

## Execution layers

1. **Preflight and identity.** Verify source/package/native/fixture identities, native exports, target framework, OS/runtime versions, and a fresh evidence directory. Do not mark any case as runtime-executed.
2. **Bounded functional cases.** Run the shape/argument, assign-to, graph lease/edit, TensorFlow, quantization, context, custom-op, negative, concurrency, and representative ABI cases from the matrix. Record status codes, expected output/reference values, ownership counters where available, and the last completed synchronization/copy boundary.
3. **Independent review.** Recompute every artifact hash, verify the exact case set and candidate label, inspect sensitive fields, and compare result criteria with raw logs. Only this review may create a separate promotion record.
4. **Optional extended layers.** Long-run, timing, restart, or device-memory work requires a new authorization decision and separate evidence schema. It is not implied by passing the bounded M12 cases.

## Case groups

| Group | Cases | Required evidence |
| --- | --- | --- |
| Shape and arguments | Factories; Save/Load/Clone | exact shape metadata, copied values, source-disposal independence |
| Clone and graph ownership | Assign-to; parent lease; graph editing | independent handles, parent lease ordering, compile/run result |
| TensorFlow and quantization | TensorFlow parse; INT8/FP8 options | file/buffer equivalence, calibration forwarding, numeric reference |
| Context and custom-op | Context lifetime; registration/callback/Clone | queue/finish behavior, callback copy/delete balance, exception conversion |
| Negative and lifecycle | borrowed-device clone; variadic operation; module owner; concurrent Dispose | managed rejection, no guessed ABI, no leaked/double-freed owner, no deadlock |
| ABI paths | representative DllImport/LibraryImport frameworks | equivalent observations on net46, netcoreapp3.1, net7.0, and net10.0 |

## Promotion boundary

Passing local tests does not change the historical `84/107/1` high-level map or the `44/282` managed API baseline. Passing a real case also does not automatically promote a whole API family: promotion is per case and requires the source/package-bound record plus independent review. The variadic `migraphx_operation_create` and ownerless `migraphx_module_create` remain explicitly deferred unless a future header/ownership review changes their boundary.

中文摘要：M12 本地开发已经完成，但真实 MIGraphX 验证仍为 `runtime-deferred`。本计划和 `compatibility/m12-runtime-cases.json` 定义后续 Linux、固定 MIGraphX 2.15.0/ROCm 7.2.1 环境下的授权执行边界。当前 Windows 没有官方 MIGraphX provider；fake-native、托管全矩阵和本地测试结果只能证明托管 ownership/ABI 形状，不能提升为真实 runtime 证据。TensorFlow fixture、calibration map、源码/包/native 身份、原始日志和独立 review 都必须在真实执行前绑定；任何 case 只有经过独立 review 才能单独提升。
