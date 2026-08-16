# M9 cloud interface validation

The M9 official option smoke is `runtime-executed` for pushed commit `346cdd0b01a7f8039f5deb93058928403fccc7dd`. The authorized session used a clean detached checkout and the credential-free `tools/radeon/cloud-test.sh`; no address, credential, host key, or connection command is stored in the repository.

## Reviewed execution

The standard script verified source identity, the frozen header, the resolved official ELF and dependencies, all managed gates, and the generated Identity model hash. It then ran `--runtime-options-candidate` through options creation, file parse, parameter shape snapshot, GPU compile, typed input, parameter map, synchronous run, copied output, and exact reference comparison. The complete script exited 0 after 103 seconds on Ubuntu 24.04, ROCm 7.2.1, MIGraphX `2.15.0.70201-81~24.04`, and one gfx1100 GPU.

| Interface | Local contract | Reviewed official observation | Still required for semantic coverage |
| --- | --- | --- | --- |
| Default Loop iterations | value forwarding, negative rejection, EntryPoint failure | official setter accepted 10 before Identity parse | licensed Loop model with fixed trip-count cases |
| Loop iteration limit | value forwarding, negative rejection, EntryPoint failure | official setter accepted 65535 before Identity parse | Loop limit hit and overflow-safe failure cases |
| External-data path | absolute/UTF-8 validation and forwarding | model directory accepted for an inline Identity model | licensed external-data model plus payload manifest/hashes |
| Fast-math | true/false forwarding and cleanup | enabled Identity compile/run exactly matched `[0.25,-1,2,9]` | representative model, declared tolerance, raw comparisons |
| Exhaustive tuning | true/false forwarding and cleanup | disabled value was explicitly accepted | enabled compile under a predeclared time/resource budget |

The raw `official-m9-options-smoke.json` remained `runtime-options-candidate-executed-review-required`. Its archive transferred with matching SHA-256, the evidence set passed the sensitive-data scan, and independent assertions checked commit, model hash, shape, option values, output, and reference before the reviewed result was promoted. The external evidence record is `20260816-1049-346cdd0-m1-m2-m9-runtime`.

## Evidence boundary

The record proves official setter acceptance and restricted Identity integration on one exact source/native/model/environment tuple. It does not prove Loop trip-count semantics, real external payload resolution, exhaustive tuning enabled, fast-math accuracy on representative workloads, other GPU families, async/device-buffer paths, or performance. The five per-mapping contract levels remain `fake-native-executed` because failure injection, invalid values, and cleanup are local substitute observations; the separate runtime matrix carries the official option smoke.

## Article record

Preserve for each follow-up fixture: why the interface was selected, pinned upstream declaration/semantics, managed signature, ownership and validation policy, exact command, source/native/model identities, raw result hash, reviewed result, limitations, and resource budget. Failed samples remain part of the outer record and are not deleted to make the narrative green.

Chinese summary: the M9 official option smoke completed and was independently reviewed at `346cdd0...`. Identity proves that the five recorded settings were accepted and did not break the exact GPU result, but Loop, external-data, exhaustive-enabled, and representative fast-math semantics still require dedicated fixtures.
