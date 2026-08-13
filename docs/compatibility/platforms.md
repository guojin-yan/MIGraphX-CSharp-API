# Platforms and evidence levels

| Claim | Status | Meaning |
| --- | --- | --- |
| Managed build | M0 local validation | All exact TFMs compile when the recorded gate passes |
| Package assets | M0 local validation | Each TFM has a DLL and XML document in the candidate package |
| Clean consumer | M0 local validation | Four representative consumers restore and build from the package |
| Native loader | Planned | No native library is loaded in M0 |
| ONNX frontend | Statically verified / runtime planned | Header declarations, library dependency, and exports exist; no model was parsed |
| AMD GPU | Planned | The local machine has no AMD GPU; no cloud session was run |
| Runtime NuGet | Disabled | Native closure and licenses are incomplete; packaging fails closed |

The frozen package targets Ubuntu 24.04 amd64 metadata. That fact does not establish a supported runtime platform. Runtime support begins only after a clean environment executes a bound path and records evidence.
