# M10 authorized runtime plan

This plan is not runtime evidence. Execute it only after the Owner authorizes one exact pushed commit, the `0.9.0-rc.2` package hashes, one official host, and a bounded time window. Historical connection details and the `346cdd0...` authorization must not be reused.

## Required identity

Use a clean detached checkout. Record the 40-character source SHA; core, adapter, and HipSharp package hashes; normalized package identities; Ubuntu/kernel/CPU/RAM; GPU architecture; ROCm and MIGraphX package versions; resolved `libmigraphx_c.so.3`; header, ELF, dependency, process-map, model, and input hashes; command; UTC interval; and exit code. Verify the frozen header and all five reviewed official ELF exports before execution.

## Registry sequence

Call `GetRegisteredOperators` through the final managed package. Preserve the native count, ordered name-list SHA-256, UTF-8 validation result, duplicate count, first/last names, and a redacted raw artifact. Do not publish a full list until reviewed for disclosure. Repeat the query around the fixed Identity file and buffer parse/compile/run paths and prove that output still exactly matches `[0.25,-1,2,9]`.

The result may be described only as the parser names returned by this exact MIGraphX build. It cannot become a general ONNX support or opset claim.

## Adopted comparison sequence

For arguments, create independent host-backed values with the same shape/data, one-element-different data, and different shapes. Verify true/false results, then dispose each side at a safe call boundary and verify managed post-dispose failure. Do not compare device pointers.

For programs, compare independent empty programs; independent parses of the same Identity model; a clearly different parsed input shape where accepted; and the same pair before/after compilation. Record when printed structural equality changes. Do not describe the result as model hashing, graph isomorphism, semantic equivalence, output equality, or compiled-binary equality.

`migraphx_shape_equal` remains planned and is not executed as an adopted M10 feature. Static source evidence may be rechecked, but it must not be promoted to `fake-native-executed` or `runtime-executed` in the M10 map.

## Review and promotion

Run the final managed, ABI/export, package, consumer, and documentation gates in the detached checkout before the runtime probes. Return raw JSON, logs, exit codes, and every identity hash through the authorized evidence channel. Keep candidate records at `runtime-candidate-executed-review-required` until an independent review rechecks source/native/package identities, registry hash/count, comparison cases, Identity output, sensitive-data scan, and command exit status.

Expected runtime after an already prepared official host is 15-25 minutes. Environment installation or repair is outside that estimate and requires separate approval. Any source, API, package, or documentation change invalidates the runtime candidate.

中文摘要：该计划只在 Owner 对最终 pushed SHA、包 hash、主机和时间窗重新授权后执行。官方会话需记录 registry count/list hash、Identity 不回归、argument/program 明确相等与不等用例，并经独立复核后才能提升；shape equality 仍不执行。
