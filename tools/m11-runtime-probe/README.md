# M11 package-only runtime probe

This probe is source for a separately authorized official session. Its project has only exact package references to core/adapter `0.9.0-rc.8` and HipSharp `0.9.1`; it has no project reference or native payload.

`run.sh` requires a clean detached exact source SHA, transferred package hashes, fixed header, explicit MIGraphX/HIP paths, deterministic fixtures, a new evidence-record directory, and a local-only NuGet feed. It enforces the frozen 1,800-second session, a process-group-wide 10-second TERM-to-KILL escalation, and 120-second case boundaries, runs each repeated positive case three times, and uses a second process for the cache-restart hit. It never invokes GPU runtime inventory utilities; those belong to the separately bounded preflight. Candidate JSON is always `runtime-candidate-executed-review-required`.

The file/buffer case flushes structured start/completion markers to `raw/case-stages.jsonl` around parse, shape, compile, argument/map, run, readback, comparison, and resource teardown. A session-level KILL therefore preserves the last completed native boundary.

`review.ps1` is a separate reviewer. It recomputes package normalized identities, exact case sets, stage-trace completeness, result/artifact hashes, registry stability, source/assembly identity, timeout metadata, exit codes, and the sensitive scan before writing an external `runtime-executed` review result. It does not review or claim long-run/timing evidence.

Do not execute either script on an official host without a new Owner authorization naming the final source/package identities, host, time window, and test layer.
