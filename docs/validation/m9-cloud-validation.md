# M9 cloud interface validation

M9 cloud execution is currently `runtime-deferred`. No historical address, credential, host, or M1/M2 authorization may be reused. A run starts only after the owner authorizes one exact pushed 40-character commit and time window; `tools/radeon/cloud-test.sh` itself contains no connection logic or credentials.

## Prepared execution

The clean detached checkout gate verifies source identity, the frozen header, the resolved official ELF and dependencies, all managed gates, and the generated Identity model hash. It then runs `--runtime-options-candidate`, which exercises the public object layer through options creation, file parse, parameter shape snapshot, GPU compile, typed input, parameter map, synchronous run, copied output, and exact reference comparison.

| Interface | Local contract | Prepared cloud observation | Still required for semantic coverage |
| --- | --- | --- | --- |
| Default Loop iterations | value forwarding, negative rejection, EntryPoint failure | official setter accepts 10 before Identity parse | licensed Loop model with fixed trip-count cases |
| Loop iteration limit | value forwarding, negative rejection, EntryPoint failure | official setter accepts 65535 before Identity parse | Loop limit hit and overflow-safe failure cases |
| External-data path | absolute/UTF-8 validation and forwarding | model directory accepted for an inline Identity model | licensed external-data model plus payload manifest/hashes |
| Fast-math | true/false forwarding and cleanup | enabled Identity compile/run with exact reference | representative model, declared tolerance, raw comparisons |
| Exhaustive tuning | true/false forwarding and cleanup | disabled value is explicitly accepted | enabled compile under a predeclared time/resource budget |

The runner writes `official-m9-options-smoke.json` and marks it `runtime-options-candidate-executed-review-required`. Promotion to `runtime-executed` requires transfer hash verification, redaction, independent JSON review, and an update to `compatibility/runtime-validation-matrix.json` bound to the exact commit, native/environment identity, model hash, commands, UTC interval, and exit codes. Raw logs, connection data, hostnames, and device identifiers are not committed.

## Article record

For each interface preserve: why it was selected, pinned upstream declaration/semantics, managed signature, ownership and validation policy, local failure cases, exact cloud command, source/native/model identities, raw result hash, reviewed result, limitations, and follow-up fixture needs. Failed samples remain part of the outer record and are not deleted to make a narrative green.

中文摘要：M9 云端接口测试脚本已准备但尚未执行。新授权后，它会在 clean detached 精确提交上留下结构化 JSON；Identity 只覆盖 setter 接受与推理回归，Loop/external-data/exhaustive/精度仍需要专用模型和资源预算。
