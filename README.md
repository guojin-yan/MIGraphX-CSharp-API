<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/readme/hero-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="docs/images/readme/hero-light.svg">
  <img alt="MIGraphXSharp — AMD GPU inference for C# and .NET" src="docs/images/readme/hero-light.svg" width="100%">
</picture>

# MIGraphX C# API · MIGraphXSharp

[![NuGet](https://img.shields.io/nuget/v/JYPPX.ROCm.MIGraphX.CSharp.API.svg)](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/)
[![Downloads](https://img.shields.io/nuget/dt/JYPPX.ROCm.MIGraphX.CSharp.API.svg)](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/)
[![Release](https://img.shields.io/github/v/release/guojin-yan/MIGraphX-CSharp-API)](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-4.6_to_10.0-512BD4)](docs/compatibility/frameworks.md)
[![ROCm](https://img.shields.io/badge/ROCm-7.2.1-ED1C24)](docs/guides/runtime-deployment.md)
[![Stars](https://img.shields.io/github/stars/guojin-yan/MIGraphX-CSharp-API?style=flat)](https://github.com/guojin-yan/MIGraphX-CSharp-API/stargazers)

English | [简体中文](README.zh-CN.md)

MIGraphXSharp brings AMD MIGraphX inference to C# and .NET. Load ONNX models, compile for AMD GPUs, prepare typed tensors, and manage native resource lifetimes. The optional HIP adapter connects device inputs and outputs to your GPU processing on the same stream.

The two complete vision examples run **YOLO26 detection, classification, segmentation, pose and oriented boxes**, plus **PP-OCRv6 text detection and recognition**. Their default HIP backend keeps numerical preprocessing and postprocessing on the GPU; a C# CPU backend is also included. Running them requires no Python or OpenCV.

[Quick start](#quick-start) · [Samples](#vision-samples) · [Documentation](#documentation) · [Release notes](CHANGELOG.md) · [Report an issue](https://github.com/guojin-yan/MIGraphX-CSharp-API/issues)

## Release 0.0.2

Version 0.0.2 adds `RunDeviceIntoAsync` and complete HIP vision pipelines to the core library, optional adapter, bilingual XML documentation and two independent samples. Download packages, source and offline documentation from [v0.0.2](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v0.0.2). Version 0.0.1 remains available as the initial release.

Official runtime checks cover the supplied vision models, dynamic inputs, program persistence/cache and HIP host/device-input workflows on Ubuntu 24.04, ROCm 7.2.1, MIGraphX 2.15.0 and gfx1100. See [platform support](docs/compatibility/platforms.md) for the scope of these checks.

## Features

| Area | What you can do |
| --- | --- |
| ONNX inference | Parse from file or memory, configure input dimensions, compile a target, execute and copy typed outputs |
| Tensors and ownership | Inspect immutable shapes, create typed host arguments, use multiple inputs/outputs, dispose native resources |
| Dynamic shapes and cache | Set dimension ranges, validate concrete inputs, save/load programs and reuse compiled models |
| Graph APIs | Create program-owned modules, parameters, literals and instructions; build typed operation attributes |
| HIP integration | Submit on HipSharp streams, retain device inputs/outputs and enqueue GPU postprocessing without full host tensor copies |
| Advanced APIs | TensorFlow parsing, FP16/BF16/INT8/FP8 quantization and experimental custom-operation callbacks |
| Vision samples | YOLO26 five tasks and PP-OCRv6 tiny/small/medium; JSON, image previews, masks, text and timing |
| .NET integration | 15 target frameworks, consistent public APIs, Chinese/English XML comments and loader diagnostics |

## Packages and requirements

| Package | Version | Purpose |
| --- | --- | --- |
| [JYPPX.ROCm.MIGraphX.CSharp.API](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/0.0.2) | 0.0.2 | Core inference and managed resource API |
| [JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop/0.0.2) | 0.0.2 | Optional stream/device-input integration; exact core and HipSharp 0.10.0 dependencies |

The C# namespace is <code>JYPPX.ROCm.MIGraphXSharp</code>. Both packages contain managed assemblies for <code>net46</code>–<code>net481</code>, <code>netcoreapp3.1</code> and <code>net5.0</code>–<code>net10.0</code>. The vision samples target .NET 10.

| Component | Validated baseline |
| --- | --- |
| Operating system | Ubuntu 24.04 x86-64 |
| AMD runtime | ROCm 7.2.1 and MIGraphX 2.15.0.70201-81~24.04 |
| GPU architecture | gfx1100 |
| Native library | /opt/rocm/lib/libmigraphx_c.so.3 |

Install ROCm/MIGraphX from [AMD's official system repository](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html). The NuGet packages do not bundle AMD native libraries. Windows managed builds are available; an official Windows MIGraphX runtime is not supplied. Other devices and runtime versions need application validation.

## Quick start

### 1. Install

~~~bash
dotnet new console -n MigraphxDemo
cd MigraphxDemo
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API --version 0.0.2

# Optional HIP stream and device-input integration:
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop --version 0.0.2
~~~

Offline installation is also available from the Release assets. See [Getting started](docs/guides/getting-started.md).

### 2. Run an ONNX model

For an existing model with one static, standard float32 input/output and four input elements:

~~~csharp
using JYPPX.ROCm.MIGraphXSharp;

var result = MIGraphXOnnxWorkflow.RunFile(
    "/opt/rocm/lib/libmigraphx_c.so.3",
    "/absolute/path/to/model.onnx",
    new[] { 1f, 2f, 3f, 4f });

Console.WriteLine(string.Join(", ", result.Output));
~~~

Use absolute paths and supply the exact number of input elements required by your model. This helper compiles for GPU with host offload-copy and returns a managed output copy. For image models, begin with the samples below; for multiple inputs/outputs, use [managed objects](docs/guides/managed-objects.md).

### 3. Diagnose the runtime

~~~csharp
var report = MIGraphXEnvironment.Probe("/opt/rocm/lib/libmigraphx_c.so.3");
Console.WriteLine(report.State);
foreach (var diagnostic in report.Diagnostics)
    Console.WriteLine(diagnostic);
~~~

See [deployment and troubleshooting](docs/guides/runtime-deployment.md) for native library loading and runtime setup.

## Vision samples

Each sample contains its own complete image, inference, postprocessing and timing code. They reference this library and ImageSharp, with no shared sample project. Supply your own ONNX models, images, labels and OCR dictionary.

| Project | Task | Typical input | Output |
| --- | --- | --- | --- |
| [Yolo26](samples/Yolo26/README.md) | Detection | 640 × 640 | Boxes, classes and scores |
| Yolo26 | Classification | 224 × 224 | Top-5 classes |
| Yolo26 | Instance segmentation | 640 × 640 | Per-instance masks and boxes |
| Yolo26 | Pose | 640 × 640 | 17 keypoints per person and skeleton |
| Yolo26 | Oriented boxes | 1024 × 1024 | Rotated quadrilaterals |
| [PaddleOcrV6](samples/PaddleOcrV6/README.md) | Text detection + recognition / recognition only | Detector sizes aligned to 32; recognition 48 × 320 | Text, quadrilaterals, crops and confidence |

Run from the repository root on the configured Linux host:

~~~bash
dotnet run --project samples/Yolo26 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 --task detect \
  --backend hip --gpu-architecture gfx1100 \
  --model /models/yolo26n.onnx --image /images/bus.jpg \
  --output /results/yolo26 --warmup 5 --iterations 30

dotnet run --project samples/PaddleOcrV6 -c Release -- \
  --native /opt/rocm/lib/libmigraphx_c.so.3 \
  --backend hip --gpu-architecture gfx1100 \
  --det-model /models/PP-OCRv6_tiny_det_inference.onnx \
  --rec-model /models/PP-OCRv6_tiny_rec_inference.onnx \
  --dictionary /models/PP-OCRv6_tiny_rec_dict.txt \
  --image /images/document.jpg --output /results/ocr
~~~

Change the YOLO task and matching model for other tasks. OCR small/medium use the same entry point with matching models/dictionaries. Omit the detector argument to recognize a cropped text line. Both projects save JSON, PNG and standalone SVG; segmentation also saves masks, and OCR saves text/crops.

For the tested pose model on MIGraphX 2.15.0, set <code>MIGRAPHX_DISABLE_REDUCE_FUSION=1</code> before starting the process. Model weights and sample images are not redistributed.

### Measured image latency

Ubuntu 24.04, ROCm 7.2.1, MIGraphX 2.15.0, gfx1100, 16-core CPU quota, float32; 5 warmups and 30 measured iterations per case. Both paths use MIGraphX GPU inference. The table compares whole-image wall-clock latency with CPU versus HIP preprocessing/postprocessing.

| Model / workload | CPU pre/post pipeline, ms | HIP pipeline, ms | HIP P95, ms | Speedup |
| --- | ---: | ---: | ---: | ---: |
| YOLO26 n detection | 15.77 | 2.75 | 2.83 | 5.74× |
| YOLO26 s detection | 18.57 | 4.46 | 4.56 | 4.17× |
| YOLO26 s classification | 5.46 | 1.50 | 1.53 | 3.65× |
| YOLO26 s segmentation | 61.30 | 7.44 | 7.55 | 8.23× |
| YOLO26 s pose | 18.54 | 4.97 | 5.04 | 3.73× |
| YOLO26 s oriented boxes | 42.25 | 7.24 | 7.29 | 5.84× |
| PP-OCRv6 tiny / 16 lines | 66.09 | 10.97 | 11.08 | 6.02× |
| PP-OCRv6 small / 16 lines | 107.51 | 16.98 | 17.06 | 6.33× |
| PP-OCRv6 medium / 16 lines | 118.68 | 31.63 | 31.88 | 3.75× |

YOLO uses batch=1. OCR detection uses batch=1; the CPU baseline recognizes lines sequentially, while HIP recognition uses batch=4 and 2 independent instances for all 16 lines. The HIP pipeline includes upload, GPU processing, model execution, D2D output copies and compact result readback; the CPU path includes C# processing and full tensor readback. Both exclude file decode, model/kernel compilation, drawing and file output. GPU clocks/power were not locked. OCR batch/instance counts also affect memory and compilation cost; use the [sample timing options](docs/guides/vision-samples.md) for your workload.

## Documentation

Read the [Chinese technical blog](docs/blog/README.md) for standalone release and application articles.

| Start here | Reference |
| --- | --- |
| [Installation and first inference](docs/guides/getting-started.md) | [API reference](docs/api/index.md) |
| [Managed objects and resource ownership](docs/guides/managed-objects.md) | [Framework compatibility](docs/compatibility/frameworks.md) |
| [Dynamic inputs, persistence and cache](docs/guides/dynamic-shapes-cache.md) | [Platform support and limits](docs/compatibility/platforms.md) |
| [Typed operation attributes](docs/guides/operation-attributes.md) | [Versioning](docs/guides/api-versioning.md) |
| [HIP asynchronous execution](docs/guides/hip-interop.md) | [Deployment and troubleshooting](docs/guides/runtime-deployment.md) |
| [Graph, TensorFlow and quantization](docs/guides/advanced-apis.md) | [Vision sample guide](docs/guides/vision-samples.md) |

The Release includes offline HTML documentation. Open <code>index.html</code> after extracting the documentation archive.

## Supported scope

Common integer, float, Boolean, string, null and array attributes are available through the typed builder. Modules created or obtained through a program retain that program's lifetime. General C format varargs and independent modules without a native destroy contract are outside the public wrapper.

Experimental custom-operation callbacks have host-reference numerical checks; GPU/device callback memory and complete provider lifetimes remain unqualified. No zero-copy, overlap or arbitrary workload performance guarantee is made. OCR uses rectangle unclip and does not include page layout, orientation classification or table recognition. See the platform guide before integrating experimental paths.

## Build from source

Install the SDK selected by <code>global.json</code>, PowerShell 7 and the [HipSharp source dependency](CONTRIBUTING.md), then run:

~~~powershell
./eng/build.ps1 -Configuration Release -Version 0.0.2
./eng/pack.ps1 -Configuration Release -Version 0.0.2 -NoBuild
~~~

Development builds without an explicit version use <code>0.0.0</code>. Build output, packages and generated documentation go to the sibling <code>MIGraphXSharp.Workspace/</code> directory. [CONTRIBUTING.md](CONTRIBUTING.md) covers the complete build setup.

## Project layout

| Directory | Contents |
| --- | --- |
| src/ | Core library and optional HIP adapter |
| samples/ | Runnable C# examples |
| docs/ | User guides, deployment and API documentation |
| tests/, native/ | Managed tests and native test sources |
| eng/, tools/ | Build, packaging, generation and diagnostic tools |
| eng/contracts/ | Generator inputs, public API snapshots and native/package contracts |

## Community and license

Report problems through [GitHub Issues](https://github.com/guojin-yan/MIGraphX-CSharp-API/issues), including package/native versions, OS, GPU and a minimal reproduction. Contributions are welcome.

Related .NET inference projects: [OpenVINO C# API](https://github.com/guojin-yan/OpenVINO-CSharp-API) · [TensorRT C# API](https://github.com/guojin-yan/TensorRT-CSharp-API) · [HIP C# API](https://github.com/guojin-yan/HIP-CSharp-API).

Copyright 2026 Guojin Yan. Licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE). AMD, ROCm, MIGraphX, ImageSharp and model assets retain their respective licenses.
