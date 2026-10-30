# Native environment diagnostics

This directory contains immutable diagnostic records used by `MIGraphXEnvironment`. Reports copy their diagnostic collections so callers cannot mutate a captured result. Inspect candidate sources, load states and platform errors when diagnosing a selected native library.

Loading occurs only through explicit probe/resource operations. See [deployment and troubleshooting](../../../docs/guides/runtime-deployment.md).
