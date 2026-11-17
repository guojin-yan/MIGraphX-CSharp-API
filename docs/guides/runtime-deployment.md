# Runtime packages and deployment

Version 1.0.0 distributes managed APIs and optional platform-specific MIGraphX engine packages separately. The runtime package contains MIGraphX 2.18.0; ROCm 10.1 libraries, GPU device data and the AMD driver are system prerequisites. Package version 1.0.0 is the project release number, rather than the native engine or ROCm version.

## Select a platform

| Package | Native assets | Tested GPU | Engine source |
| --- | --- | --- | --- |
| `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.win-x64` | `runtimes/win-x64/native` | Radeon PRO W7900 / gfx1100 | Project build of AMD's rocm-10.1 source; included Windows patch |
| `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.ubuntu.24.04-x64` | `runtimes/linux-x64/native` | Radeon PRO W7900 / gfx1100 | AMD MIGraphX 2.18.0 binary; ELF search paths relocated to `$ORIGIN` |

The engine includes the C API, ONNX frontend, GPU/reference plugins, device code and HIPRTC helper. Ubuntu also supplies the TensorFlow frontend; its GPU validation used WSL2. Linux builds and published outputs create the required plugin symbolic links and helper executable permissions through the runtime package's MSBuild targets. Windows device code targets gfx1100. The Ubuntu binary contains additional GPU architectures, whose presence is not workload qualification. There is no macOS runtime package.

```bash
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API --version 1.0.0
# Choose one deployment platform:
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.win-x64 --version 1.0.0
# dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.ubuntu.24.04-x64 --version 1.0.0
```

Set the application's `RuntimeIdentifier` to `win-x64` or `linux-x64` when publishing for that platform. NuGet copies the matching native assets. The managed core remains usable with an explicitly selected coherent system MIGraphX installation; a runtime package is optional.

## Configure ROCm before process startup

Install matching ROCm 10.1 HIP/HIPRTC, COMGR, MIOpen, rocBLAS and hipBLASLt libraries, their transitive libraries, GPU-specific device data and bitcode. Install the compatible AMD GPU driver separately. On Windows also install the Visual C++ x64 Redistributable. Ubuntu requires its usual glibc, libstdc++, libgcc, zlib, libnuma, libelf and libdrm dependencies. WSL uses its host driver bridge and GPU device access.

For the independently installed SDK used in this project's Windows verification:

```powershell
$hipRoot = 'D:/AMD/ROCm/10.1.0/HIP'
$env:PATH = "$hipRoot/bin;$hipRoot/lib/llvm/bin;" + $env:PATH
$env:HIP_PATH = $hipRoot
$env:ROCM_PATH = $hipRoot
$env:HIP_DEVICE_LIB_PATH = "$hipRoot/lib/llvm/amdgcn/bitcode"
$env:ROCBLAS_TENSILE_LIBPATH = "$hipRoot/bin/rocblas/library"
$env:HIPBLASLT_TENSILE_LIBPATH = "$hipRoot/bin/hipblaslt/library/gfx1100"
```

On Ubuntu, use the corresponding installed ROCm paths:

```bash
export HIP_PATH=/opt/AMD/ROCm/10.1.0/HIP
export ROCM_PATH="$HIP_PATH"
export PATH="$HIP_PATH/bin:$HIP_PATH/lib/llvm/bin:$PATH"
export LD_LIBRARY_PATH="$HIP_PATH/lib:$HIP_PATH/lib/rocm_sysdeps/lib:$HIP_PATH/lib/host-math/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export HIP_DEVICE_LIB_PATH="$HIP_PATH/lib/llvm/amdgcn/bitcode"
export ROCBLAS_TENSILE_LIBPATH="$HIP_PATH/lib/rocblas/library"
export HIPBLASLT_TENSILE_LIBPATH="$HIP_PATH/lib/hipblaslt/library"
```

Adapt these paths to the actual coherent installation. NuGet does not install the SDK or change the machine's environment. Do not mix this ROCm 10.1 engine with the HIP ROCm 7.2.1 native NuGet package. The managed HipSharp 1.0.0 dependency has its own version and supports explicitly selected native libraries.

## Discover the engine

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var environment = MIGraphXEnvironment.ProbeSystem();
if (!environment.ObjectsExecuted)
    throw new InvalidOperationException(string.Join("; ", environment.Diagnostics.Select(d => d.Message)));
string nativePath = environment.LoadedPath!;
// Pass nativePath to the model APIs.
```

Probing checks the application RID directory, application base and system loader. An explicit absolute installed library path is also supported. Select one native family per process; restart before changing it. Native plugins and ROCm dependencies must resolve from that same family. A successful probe does not certify arbitrary models or precisions.

## Package provenance and limits

Each runtime includes `native-manifest.json` with project source commit, source archive identity, native/ROCm versions, payload hashes and host dependencies. Third-party licenses are bundled under `licenses/`. The release attaches the exact engine input archives and SHA-256 checksums; Windows is a project source build, rather than an AMD Windows installer.

The known Windows 2×2 INT8 native crash and missing Windows TensorFlow importer remain. Consult [platform limits](../compatibility/platforms.md) and [2.18 APIs](runtime-2.18.md). Installing a runtime package does not repair upstream native defects.

The original Linux ROCm 7.2.1 / MIGraphX 2.15 system deployment remains an alternative for the base API. Its bounded historical tests and explicit O2 workaround remain documented in the platform guide; it is a different native family from the new 2.18 runtime packages.

中文摘要：1.0.0 提供两个平台的 MIGraphX 2.18 引擎包；仍需安装 ROCm 10.1 计算库、设备数据和显卡驱动。Windows 构建针对 gfx1100，Ubuntu 原生资产使用 `linux-x64` RID。通过 `ProbeSystem()` 发现输出目录中的引擎；不要混装不同 ROCm 版本的原生计算库。
