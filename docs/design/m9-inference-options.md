# M9 inference-option API batch

M9 resumes high-level interface coverage on the `0.x.x` line. It projects five already generated and ABI-audited C entry points through existing resource owners: ONNX default Loop iterations, Loop iteration limit, external-data path, compile fast-math, and exhaustive tuning. The aggregate high-level inventory becomes 80 supported, 111 planned, and one unsupported item; the native inventory remains fixed at 192.

## Managed contract

`MIGraphXOnnxOptions.SetDefaultLoopIterations` and `SetLimitLoopIterations` accept non-negative `Int64` values. Upstream ROCm 7.2.1 uses defaults of 10 and `UInt16.MaxValue`; a negative shape bound has no defined managed use and is rejected before interop. `SetExternalDataPath` requires an absolute path, normalizes it, encodes strict UTF-8, and borrows the resulting pointer only during the native call. It does not require the directory to exist because deployment can create or mount it after options construction.

The existing two-argument `MIGraphXCompileOptions` constructor remains available. A four-argument overload adds explicit `fastMath` and `exhaustiveTune` values, exposed as immutable properties. Construction creates one owned native options handle and applies offload-copy, fast-math, and exhaustive-tune in order; any setter failure disposes the partially configured handle.

Fast-math permits approximate implementations and therefore needs workload-specific accuracy review. Exhaustive tuning can materially increase compile time and is never enabled implicitly. External-data models need a complete manifest over the ONNX file and every external payload before their cache identity can be considered closed; the current M5 cache helper must not be used for such a model by hashing only the ONNX protobuf.

## Evidence boundary

The fake-native substitute verifies value forwarding, strict path validation, exact EntryPoint failures, and partial-construction cleanup. `smoke/OnnxWorkflowSmokeRunner --runtime-options-candidate` is wired into the credential-free Radeon script for a future authorized official-host run. The generated Identity model can prove that the official runtime accepts these setters and that fast-math compilation still matches its exact reference. It cannot exercise Loop semantics, actual external tensors, exhaustive-tune enabled behavior, or representative-model accuracy.

中文摘要：M9 在 `0.x.x` 下新增 5 个推理 option 高层入口，累计映射为 80/111/1。local fake-native 只验证转发、校验和清理；真实接口/功能结论必须由精确提交上的新云端记录给出。Identity 只能验证官方 runtime 接受设置并完成推理，不能替代 Loop、external-data、exhaustive-tune 和代表性精度测试。
