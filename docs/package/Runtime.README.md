# MIGraphX native engine

These optional packages accompany MIGraphX CSharp API 1.0.0:

- `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.win-x64`
- `JYPPX.ROCm.MIGraphX.CSharp.API.Runtime.ubuntu.24.04-x64`

Package version **1.0.0** contains native **MIGraphX 2.18.0**, built for **ROCm 10.1.0**. It supplies the MIGraphX engine, ONNX frontend, GPU/reference plugins, device code and HIPRTC helper. The Ubuntu package also contains the TensorFlow frontend. Windows uses a project source build with the included patch; Ubuntu uses AMD's native binary with library search paths relocated to `$ORIGIN`. These are project distributions, with the exact source and file hashes in `native-manifest.json`.

Install the matching ROCm 10.1 system libraries (HIP/HIPRTC, COMGR, MIOpen, rocBLAS, hipBLASLt and their dependencies), GPU device data and a compatible AMD driver separately. The package does not install a driver or the ROCm SDK. Set the ROCm library search paths and device-data paths before starting the application. Do not mix private HIP runtime packages from ROCm 7.2.1 with this engine.

Add the core package and the runtime package for your deployment platform. Add the HIP adapter only when using HIP streams/device buffers. Select `win-x64` or `linux-x64` as the application RuntimeIdentifier; the Ubuntu package's assets use NuGet's portable `linux-x64` RID. `MIGraphXEnvironment.ProbeSystem()` discovers copied assets; an explicit absolute engine path remains supported.

The release is tested on Radeon PRO W7900 / gfx1100. Windows device code is built for gfx1100. Other GPU models need their own compatible native build and workload validation. The known Windows 2×2 INT8 native crash remains; Windows has no TensorFlow importer. No general model or precision coverage is implied.

The managed libraries are Apache-2.0. MIGraphX is MIT; bundled third-party notices are under `licenses/`. See the repository's deployment and platform guides for configuration and native feature limits.
