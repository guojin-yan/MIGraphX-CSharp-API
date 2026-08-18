# M11 official-runtime hardening plan

M11 is `runtime-deferred` until external evidence is independently reviewed. This document and `compatibility/m11-runtime-cases.json` are an executable plan, not official runtime evidence. The `0.9.0-rc.9` candidate is authorized for a bounded official session naming its final source and package identities; publication, tag, release, Pages deployment, and Actions remain out of scope.

## Evidence starting point

The source evidence model now records the independently reviewed M10 post-build promotion in `compatibility/m10-post-build-runtime-evidence.json`. It binds clean detached source `e2386dc69e7640f8ff12d95284e56c3f02c87938`, core/adapter `0.9.0-rc.2`, the reviewed result hash, and the four adopted M10 entry points. It does not mutate the historical package or claim that its build-time JSON already contained runtime results. `migraphx_shape_equal` remains `retained-planned`.

M11 does not adopt any C API item or change the public surface. The expected baseline remains core `27 types / 160 members`, adapter `3 / 11`, and aggregate map `84 supported / 107 planned / 1 unsupported`.

The immutable rc.6 official functional record completed all 20 attempted cases: 18 passed and two dynamic-shape cases failed before their intended boundaries because `NativeShapeSnapshot.Create` called `migraphx_shape_lengths` before `migraphx_shape_dynamic`. MIGraphX 2.15.0 rejected `lens()` on a dynamic shape with status 4. Cache restart and independent promotion review were correctly blocked, so no individual rc.6 case is promoted. Rc.7 queries type and dynamic state first, reads native dynamic dimensions only on the dynamic branch, and reserves lengths, strides, elements, bytes, and standard-layout accessors for the static branch. Fake-native now enforces the same constraint.

## Candidate and fixtures

Any tracked M11 change creates a new managed candidate. The package-only probe under `tools/m11-runtime-probe/` must restore the exact `0.9.0-rc.9` core, adapter, and HipSharp packages from a transferred local feed. Project references and source fallback are forbidden for the official session.

`eng/generate-m11-fixtures.ps1` creates three deterministic, project-owned Apache-2.0 ONNX fixtures:

| Fixture | SHA-256 | Reference |
| --- | --- | --- |
| Identity float32 `[1,4]` | `0b6fa0302a08a3fccf375d8ce4f84b7da59ccfa742fc59a0baa5f31722ae75f9` | exact identity |
| Identity + Neg float32 `[2,2]`, two ordered outputs | `0c703e1455a35c103496ff8173a1c7f2738c878cb394aba83e2934e06008bb9a` | exact identity and negation arrays |
| Dynamic override Identity float32 `[1,4]` | `83fbdfae497dd5a03620be5c16f05642a5d367295816ee5e448fd1731ff2993b` | runtime static and dynamic batch 1/2 overrides preserve values |

The binaries are generated into `artifacts/`, never tracked or packed. Registry count/hash may be sampled before and after the probe only as an environment-drift signal. It is not a fixture capability promise and the full registry remains restricted.

## Bounded functional layer

The functional layer has a frozen 1,800-second session timeout, a process-group-wide 10-second TERM-to-KILL escalation, a 120-second default case timeout, three iterations for repeated positive cases, zero unexpected errors, zero output mismatches, and zero remaining cache temporary files. The file/buffer path writes durable JSONL markers around parse, shape, compile, argument/map, run, readback, content comparison, and each resource teardown so a killed native worker still leaves a precise last-stage boundary. Its case matrix records fixture identity, public API path, expected boundary, synchronization and copy boundaries, ownership, prerequisites, environment-change requirement, evidence level, and every uncovered boundary.

The runner does not invoke `rocminfo`, `rocm-smi`, or another GPU runtime inventory utility. Those observations belong to a separately bounded preflight because the rc.5 environment demonstrated that a passive inventory process can enter uninterruptible kernel state. The functional probe continues to load only the explicitly hash-verified MIGraphX and HIP ELF paths.

M4 covers explicit lifecycle and Dispose, file/buffer parsing, parameter/output snapshots, typed owned arguments, exact maps, compile/run/deep copy, and the two-output Identity+Neg order and independent lifetime. Safe negatives are post-Dispose access, wrong input names, value-count mismatch, and uncompiled Run. A second official native root remains deferred.

M5 covers static `[1,4]`/`[2,4]` overrides, dynamic `[1..2,4]` execution with exact references, msgpack Save/Load/recompile/run, cache cold rebuild/warm hit, model/options key changes, payload and sidecar corruption recovery, same-key concurrent writers, temporary-file cleanup, and a fresh-process cache hit. Cache roots must be new absolute children of the evidence record. A true native-identity variation requires an audited alternate provider and remains deferred.

M6 covers host async passive state, query/synchronize completion, same-stream multiple runs, distinct-stream isolation, early caller disposal, device input with `offloadCopy=false`, explicit H2D/D2H, and safe name/shape/capacity/runtime-client/mode rejection. Every success is decided after real stream completion. Enqueue time is not inference time; device-pointer use is not a zero-copy claim. Completion/D2H failure injection and wrong-device topology changes remain substitute-only unless separately shown safe and authorized.

Probe JSON must use `runtime-candidate-executed-review-required`. Only an independent review may promote reviewed cases in an external record. The runner cannot write `runtime-executed`.

## Isolation, restart, long-run, and timing

Wrong architecture, missing dependency/export, mixed ROCm patch, and already-loaded second-root cases require pre-audited isolated fixtures and separate Owner authorization. The session must never replace `/opt/rocm`, inject unknown binaries, weaken loader/TLS/host-key controls, or reuse an old endpoint or authorization.

Fresh-process cache restart is part of bounded functional validation. Host restart, long-run, and timing remain a second authorization layer. The frozen long-run plan is a 10-minute preflight, three 60-minute phases, process restart every 10 minutes, one authorized host restart between managed and async blocks, and a final 30-minute mixed phase within a 300-minute reservation. Timing remains 20 warmups plus 200 samples in each of five fresh processes with recorded randomized order. Review recomputes count, failures, median, p95, minimum, maximum, and MAD from raw samples.

Resource recovery allows the greater of 128 MiB or 15% RSS growth, and the greater of 64 MiB or 10% GPU-memory growth, after disposal and the declared recovery interval. Unexpected errors and reference mismatches must remain zero. CPU, RSS, GPU memory, disposal recovery, clocks, power, and system noise are mandatory observations. These thresholds may not be lowered after execution starts.

## Windows fixed-version policy

The AMD ROCm 7.2.1 compatibility page links MIGraphX 2.15.0 and separately routes Windows installation to the HIP SDK documentation. The fixed MIGraphX 2.15.0 documentation explicitly says it applies to Linux. AMD's Windows component-support table says the Windows HIP SDK is a subset and marks AI libraries, including MIGraphX on the Linux side of the comparison, as not available on Windows. The official Windows 7.2.1 repository exposes ROCm SDK aggregate artifacts, but does not override the documented component-support boundary.

Accordingly, the fixed ROCm 7.2.1/MIGraphX 2.15.0 Windows native provider is `not-applicable` for this project's runtime matrix. No Windows runtime session is designed and no DLL is downloaded or installed. HIP SDK availability is not treated as MIGraphX availability.

Official sources, retrieved 2026-08-16:

- [ROCm 7.2.1 compatibility matrix](https://rocm.docs.amd.com/en/docs-7.2.1/compatibility/compatibility-matrix.html)
- [MIGraphX 2.15.0 documentation](https://rocm.docs.amd.com/projects/AMDMIGraphX/en/docs-7.2.1/index.html)
- [HIP SDK for Windows component support](https://rocm.docs.amd.com/projects/install-on-windows/en/latest/conceptual/component-support.html)
- [AMD Windows ROCm 7.2.1 repository index](https://repo.radeon.com/rocm/windows/rocm-rel-7.2.1/)

## Required official record and review

An authorized session must bind the final pushed 40-character SHA, clean detached checkout, three package ZIP hashes and normalized identities, frozen header, resolved ELF/SONAME/exports/dependency closure/process map, OS/kernel/CPU/RAM/cgroup/GPU/ROCm/MIGraphX/.NET identities, all fixture/input/cache/script hashes, exact command, UTC interval, and exit codes. Raw evidence belongs in a new `Radeon_Cloud/records/<timestamp>-<sha>-m11-.../` record and must include the copied probe source, logs/JSON, hashes, and independent review script/result.

Until bounded M4-M6 official functional evidence and the separately decided long-run/timing layer are reviewed, M11 remains `runtime-deferred` and M8 remains `release-candidate-local` rather than `release-ready`.

中文摘要：M11 已冻结 package-only 的 M4-M6 功能、负向、cache restart、长跑、计时和资源阈值方案，并依据 AMD 官方资料把固定版本 Windows MIGraphX provider 标为 `not-applicable`。rc.6 官方功能会话以 18/20 失败闭合；rc.7 修复动态 shape 快照访问顺序，但当前没有新官方授权，所有 M4-M6 官方 case 仍为 `runtime-deferred`，M8 状态不提升。
