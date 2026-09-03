# M12 cross-target package probe

This package-only helper is executed by `tools/m12-runtime-probe/run.sh --include-deferred`. It restores the exact source-bound core package and runs the same bounded Identity and materialized-operation checks under `netcoreapp3.1`, `net7.0`, and `net10.0`.

The probe reflects the internal compile-time strategy marker from the packaged core assembly. The reviewer requires `DllImport` for `netcoreapp3.1` and `LibraryImport` for `net7.0` and `net10.0`, plus exact Identity output and successful reshape creation/cloning. Each target writes a separate JSON record and runs under a 180-second timeout with a 10-second TERM-to-KILL escalation.

The project intentionally has an exact `PackageReference` and no `ProjectReference`, native payload, package publication path, or promotion logic. A passing result remains `runtime-candidate-executed-review-required`; it does not change `m12-cross-target-abi` from `runtime-deferred` until an authorized real-runtime record is independently reviewed.
