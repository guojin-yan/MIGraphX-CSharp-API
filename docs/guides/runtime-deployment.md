# Runtime deployment and troubleshooting

M7 does not ship a Runtime nupkg. Use the managed core and optional adapter exactly as before, with either an explicit absolute MIGraphX library path or a system installation. Do not add the deferred Runtime project or copy files from `.cache`, `artifacts/runtime-staging`, a Debian extraction tree, or another ROCm patch into an application.

## System-native mode

`MIGraphXEnvironment.ProbeSystem` checks the application RID directory, application base, and system loader in a fixed order. `MIGraphXEnvironment.Probe` and higher-level workflows accept an absolute root-library path. The loader does not mutate `PATH` or `LD_LIBRARY_PATH`, download files, or imply that Windows/macOS candidates are supported.

System-native mode is not package-only evidence. The application is responsible for a coherent MIGraphX/ROCm installation and for the Ubuntu/system/driver boundary described in the platform matrix.

## Reserved future package mode

`runtimes/linux-x64/native/lib` is reserved for an audited package. If that directory exists, the root and `migraphx-runtime-closure.xml` must be complete. The marker describes a single ROCm 7.2.1 family and allowlists every native file. A partial or invalid reserved layout blocks fallback to a system library.

Do not create the marker by hand. Its future candidate form must be generated from a complete, licensed staging tree and bound to the exact manifest content digest. The tracked manifest records marker status `not-generated-runtime-deferred`.

## Failure interpretation

| Diagnostic | Meaning | Action |
| --- | --- | --- |
| `MIGRAPHX1001` during pack | Runtime closure/promotion state is not authorized | Keep using managed/system-native mode; close the Resume blockers before packing |
| reserved Runtime root missing | A partial package-like directory exists | Remove the partial deployment or restore the exact reviewed package; do not fall back manually |
| closure marker missing or rejected | Package identity, hash, SONAME, RID, family, or path validation failed | Treat the deployment as tampered/incompatible and restore exact assets |
| dependency missing during native load | The OS loader could not resolve a transitive library | Inspect the recorded candidate and platform error; do not add arbitrary directories to global search paths |
| different native library already active | A package/system or directory mix was attempted | Restart with one coherent native family and one source directory |
| system candidate not available | No package mode was selected and the host loader found no usable MIGraphX root | Install the exact compatible system runtime or use an authorized explicit path |

The structured diagnostics contain candidates, sources, existence, classifications, and platform messages. A package-success claim additionally requires exact file hashes, loader trace, and process maps from an authorized package-only host; diagnostics alone are not such evidence.

中文摘要：当前只能使用显式或 system-native 模式。`native/lib` 是未来 Runtime 包的保留目录，发现残缺或篡改布局时会阻止系统回退；不要手工拼接 ROCm 文件或 marker。
