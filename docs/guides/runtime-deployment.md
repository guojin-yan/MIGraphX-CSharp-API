# System-native deployment and troubleshooting

MIGraphXSharp distributes managed assemblies only. It does not publish a Runtime nupkg, copy AMD binaries into the managed packages, or stage a native closure during build. Install MIGraphX and ROCm through AMD's official system installation path before running the managed API.

## Install the audited baseline

The first validated baseline is Ubuntu 24.04 amd64, ROCm 7.2.1, and MIGraphX `2.15.0.70201-81~24.04`. Follow AMD's [official ROCm 7.2.1 Linux quick-start guide](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html) to configure the repository and signing key. The exact MIGraphX package recorded by this repository is:

```bash
sudo apt-get update
sudo apt-get install migraphx-rpath7.2.1=2.15.0.70201-81~24.04
```

Let APT resolve the declared ROCm dependencies from the same AMD repository. Do not copy files from `.cache`, `artifacts`, an extracted `.deb`, another machine, or a different ROCm patch into the application.

The native package is outside the managed NuGet lifecycle. Upgrading MIGraphX/ROCm therefore requires an application compatibility review and a fresh environment probe; a managed package update does not upgrade the native runtime.

## Select the native root

For deterministic selection, pass the absolute installed SONAME path:

```csharp
var report = MIGraphXEnvironment.Probe(
    "/opt/rocm-7.2.1/lib/libmigraphx_c.so.3");
```

`MIGraphXEnvironment.ProbeSystem` checks the application RID directory, application base, and OS loader in a fixed order. The loader does not mutate `PATH` or `LD_LIBRARY_PATH`, download native files, or install system packages. Windows and macOS candidates remain diagnostic-only; this policy does not claim an official native runtime for those platforms.

The optional HipSharp adapter must use the same coherent ROCm installation and device. Do not combine a system MIGraphX installation with private HIP/HSA/COMGR files copied into the application output.

## Verify the host

Before a real workload, verify the resolved root and its transitive dependencies with platform tools, then run the environment probe:

```bash
readelf -d /opt/rocm-7.2.1/lib/libmigraphx_c.so.3
ldd /opt/rocm-7.2.1/lib/libmigraphx_c.so.3
```

The host supplies Ubuntu system libraries, the `amdgpu`/`amdkfd` kernel drivers, firmware, `/dev/kfd`, and `/dev/dri`. Installing a user-mode package cannot replace those host requirements.

## Failure interpretation

| Diagnostic | Meaning | Action |
| --- | --- | --- |
| `MIGRAPHX1001` with `pack.ps1 -Runtime` | Runtime NuGet distribution is intentionally unsupported | Install MIGraphX/ROCm from AMD's official repository |
| explicit file not found | The requested installed SONAME path is absent | Check the selected ROCm version and installed MIGraphX package |
| dependency missing | The OS loader cannot resolve the coherent official closure | Repair the AMD system installation; do not add arbitrary copied directories |
| bad image or architecture | The native file does not match the process/host | Install the amd64 package on a supported Linux x86-64 host |
| required export missing | The installed MIGraphX version does not match the frozen C API | Restore the audited version or re-run the compatibility process for an upgrade |
| different library already active | The process already loaded a different native root | Restart and select one installation before using the managed API |

Structured diagnostics report candidates, sources, existence, classifications, and the original platform error. They do not turn untested versions, devices, models, M6 async/device buffers, zero-copy, or performance into supported claims.

中文摘要：项目只分发托管程序集。请通过 AMD 官方系统仓库安装同一版本族的 MIGraphX/ROCm，优先传入 `/opt/rocm-7.2.1/lib/libmigraphx_c.so.3` 绝对路径；不要从缓存、Debian 解包目录或不同 ROCm 版本拼装应用私有闭包。
