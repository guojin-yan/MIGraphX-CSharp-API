<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/readme/hero-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="docs/images/readme/hero-light.svg">
  <img alt="MIGraphXSharp — AMD GPU inference for C# and .NET" src="docs/images/readme/hero-light.svg" width="100%">
</picture>

<h1 align="center">MIGraphX C# API · MIGraphXSharp</h1>

<p align="center">Run ONNX models on AMD GPUs from C#, with MIGraphX inference and HIP image processing.</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache--2.0-blue.svg" alt="Apache-2.0 license"></a>
  <a href="https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/"><img src="https://img.shields.io/nuget/v/JYPPX.ROCm.MIGraphX.CSharp.API.svg" alt="NuGet version"></a>
  <a href="https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/"><img src="https://img.shields.io/nuget/dt/JYPPX.ROCm.MIGraphX.CSharp.API.svg" alt="NuGet downloads"></a>
  <a href="https://github.com/guojin-yan/MIGraphX-CSharp-API/releases"><img src="https://img.shields.io/github/v/release/guojin-yan/MIGraphX-CSharp-API" alt="GitHub Release"></a>
  <a href="https://github.com/guojin-yan/MIGraphX-CSharp-API/stargazers"><img src="https://img.shields.io/github/stars/guojin-yan/MIGraphX-CSharp-API?style=flat&amp;logo=github" alt="GitHub stars"></a>
  <a href="docs/compatibility/frameworks.md"><img src="https://img.shields.io/badge/.NET-4.6_to_10.0-512BD4" alt=".NET 4.6 to 10.0"></a>
  <a href="docs/guides/runtime-deployment.md"><img src="https://img.shields.io/badge/ROCm-7.2.1-ED1C24" alt="ROCm 7.2.1"></a>
</p>

<p align="center"><strong>English</strong> | <a href="README.zh-CN.md">简体中文</a></p>

## 📖 Introduction

[AMD MIGraphX](https://rocm.docs.amd.com/projects/AMDMIGraphX/en/latest/) is a graph inference engine in the ROCm ecosystem. It compiles deep learning models into GPU programs and optimizes their execution for AMD hardware.

**MIGraphX C# API is a .NET wrapper for AMD MIGraphX. It lets C# developers load ONNX models, compile GPU programs, work with tensors and run inference directly from their applications.** The optional HIP adapter connects GPU preprocessing, model execution and postprocessing on the same stream.

The repository includes complete **YOLO26** and **PP-OCRv4/v5/v6** examples. All three OCR generations use one PaddleOcr project. Image processing, inference, result decoding and timing are written in C# and HIP; running the examples requires no Python or OpenCV.

| Core advantage | What it means for your application |
| --- | --- |
| 🚀 AMD GPU inference | Use MIGraphX model compilation and execution through a managed C# API |
| ⚡ GPU image pipelines | Keep numerical preprocessing and postprocessing on the GPU with HIP streams and device buffers |
| 🧩 .NET integration | 15 target frameworks from .NET Framework 4.6 to .NET 10, with Chinese/English XML API comments |
| 📚 Complete examples | YOLO26 five tasks and PP-OCRv4/v5/v6 detection/recognition, including OCR batching and multiple recognition instances |

## 📢 0.0.2 Update Summary

- Adds <code>RunDeviceIntoAsync</code> for device output buffers and subsequent HIP processing.
- Moves numerical preprocessing and postprocessing in both vision examples to HIP.
- Adds OCR recognition batching and multiple instances; full-image recognition defaults to batch 4 with 2 instances.

[Complete 0.0.2 release notes](docs/release-notes/0.0.2.md) · [All releases](docs/release-notes/README.md) · [Download v0.0.2](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases/tag/v0.0.2)

## 🚀 Quick Start

### 1. Install the NuGet package

Install ROCm 7.2.1 and MIGraphX 2.15.0 on a compatible Linux host first; see the [runtime deployment guide](docs/guides/runtime-deployment.md). Then create your application:

~~~bash
dotnet new console -n MigraphxDemo
cd MigraphxDemo
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API --version 0.0.2
~~~

### 2. Write inference code

This example uses a static float32 ONNX model whose input is named <code>input</code> and contains four elements. Replace the model path, input name and data with those required by your model.

~~~csharp
using JYPPX.ROCm.MIGraphXSharp;

const string nativePath = "/opt/rocm/lib/libmigraphx_c.so.3";

// Parse the model and read its input shapes.
using var options = new MIGraphXOnnxOptions(nativePath);
using var program = MIGraphXProgram.ParseOnnxFile("/models/model.onnx", options);
var inputShapes = program.GetParameterShapes();

// Compile once, then reuse the program for subsequent inputs.
using var target = new MIGraphXTarget(nativePath, "gpu");
using var compileOptions = new MIGraphXCompileOptions(nativePath, offloadCopy: true);
program.Compile(target, compileOptions);

// Supply the model input and execute inference.
using var input = MIGraphXArgument.Create(
    nativePath, inputShapes["input"], new[] { 1f, 2f, 3f, 4f });
using var parameters = new MIGraphXParameterMap(nativePath);
parameters.Add("input", input);

using var outputs = program.Run(parameters);
float[] result = outputs[0].ToArray<float>();
Console.WriteLine(string.Join(", ", result));
~~~

### 3. Run the application

~~~bash
dotnet run -c Release
~~~

Start with the [YOLO26 example](samples/Yolo26/README.md) or [PaddleOCR example](samples/PaddleOcr/README.md) for image models. The [getting started guide](docs/guides/getting-started.md) covers installation and native-library diagnostics.

## ✨ Features

| Feature | Description |
| --- | --- |
| ONNX inference | Parse models from files or memory, compile for GPU and execute with multiple inputs/outputs |
| Typed tensors | Inspect shapes, types and strides; create typed host inputs and copy results into C# arrays |
| Dynamic shapes | Configure fixed input overrides or dimension ranges before parsing |
| Program reuse | Save/load compiled programs and use an explicit model cache |
| HIP integration | Host/device-input asynchronous submission, stream synchronization and caller-owned device output buffers |
| Graph APIs | Program-owned modules, parameters, literals, instructions and typed operation attributes |
| Advanced APIs | TensorFlow parsing, FP16/BF16/INT8/FP8 quantization and experimental custom-operation callbacks |
| Resource management | Deterministic <code>Dispose</code>, native loading diagnostics and resource leases for pending HIP work |

See the [API guides](docs/index.md) and [platform support](docs/compatibility/platforms.md) for the applicable runtime and experimental feature boundaries.

## 📦 NuGet Packages

| Package | Purpose | Version |
| --- | --- | --- |
| <code>JYPPX.ROCm.MIGraphX.CSharp.API</code> | Core model, tensor, compilation and inference API | [![NuGet](https://img.shields.io/nuget/v/JYPPX.ROCm.MIGraphX.CSharp.API.svg)](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/) |
| <code>JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop</code> | Optional HIP stream/device-buffer adapter | [![NuGet](https://img.shields.io/nuget/v/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.svg)](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop/) |
| <code>JYPPX.ROCm.HIP.CSharp.API</code> | HipSharp runtime, memory, stream and kernel APIs; adapter dependency | [![NuGet](https://img.shields.io/nuget/v/JYPPX.ROCm.HIP.CSharp.API.svg)](https://www.nuget.org/packages/JYPPX.ROCm.HIP.CSharp.API/) |

Install the adapter only when you need HIP integration:

~~~bash
dotnet add package JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop --version 0.0.2
~~~

The adapter requires core <code>0.0.2</code> and HipSharp <code>0.10.0</code>; NuGet restores these dependencies. The core namespace is <code>JYPPX.ROCm.MIGraphXSharp</code>. AMD native libraries are installed separately. [GitHub Releases](https://github.com/guojin-yan/MIGraphX-CSharp-API/releases) also provide packages, source and offline HTML documentation.

## 🧪 Examples

| Example | Tasks | Included functionality |
| --- | --- | --- |
| [YOLO26](samples/Yolo26/README.md) | Detection, classification, instance segmentation, pose and oriented boxes | HIP resize/normalization, output decoding, masks, keypoints and annotated results |
| [PP-OCRv4/v5/v6](samples/PaddleOcr/README.md) | v4/v5 mobile and server; v6 tiny, small and medium; full images or recognition only | HIP text-box extraction, perspective crops, batched recognition, multiple instances and CTC decoding |

Both projects contain their own complete implementation and target .NET 10. They include JSON/PNG/SVG output, warmup and repeated timing, and a CPU preprocessing/postprocessing option for comparison. Both backends use MIGraphX GPU model execution. Supply the ONNX models, test images and OCR dictionary separately.

The unified `PaddleOcr` entry point and v4/v5 support are available in the current main source. The published v0.0.2 tag retains the original `PaddleOcrV6` example.

On the documented gfx1100 FP32 baseline, whole-image HIP processing averages **2.75 ms for YOLO26n detection** and **10.97 ms for PP-OCRv6 tiny with 16 text lines**. These timings exclude file decode, compilation, drawing and export. The [0.0.2 notes](docs/release-notes/0.0.2.md#性能参考) contain the complete results and measurement conditions; the [vision guide](docs/guides/vision-samples.md) explains the options.

## 📖 Documentation

| Resource | Contents |
| --- | --- |
| [Getting started](docs/guides/getting-started.md) | Install packages and run the first inference |
| [Managed objects](docs/guides/managed-objects.md) | Models, tensors, inputs/outputs and resource ownership |
| [Dynamic shapes and cache](docs/guides/dynamic-shapes-cache.md) | Input overrides, program persistence and cache reuse |
| [HIP integration](docs/guides/hip-interop.md) | Asynchronous execution and GPU buffers |
| [Advanced APIs](docs/guides/advanced-apis.md) | Graphs, TensorFlow and quantization |
| [API reference](docs/api/index.md) | API documentation and offline reference |
| [Release history](docs/release-notes/README.md) | Changes, compatibility and upgrade instructions by version |
| [Technical blog](docs/blog/README.md) | Chinese project and application articles |

## 🚀 More Usage Examples

### Override an input shape

Set your model's input dimensions before parsing; compile and run it using the same object API shown above.

~~~csharp
using var options = new MIGraphXOnnxOptions(nativePath);
options.SetInputParameterShape("images", new long[] { 1, 3, 640, 640 });
using var program = MIGraphXProgram.ParseOnnxFile("/models/yolo26n.onnx", options);
~~~

### Save a compiled program

~~~csharp
using var fileOptions = new MIGraphXFileOptions(nativePath, "msgpack");
program.Save("/models/compiled.msgpack", fileOptions);
using var restored = MIGraphXProgram.Load("/models/compiled.msgpack", fileOptions);
~~~

Use a compatible native runtime and compilation target when reusing a saved program. Device output and GPU postprocessing are covered in the [HIP guide](docs/guides/hip-interop.md).

## 💻 System Requirements

| Component | Requirements |
| --- | --- |
| Managed library | .NET Framework 4.6–4.8.1, .NET Core 3.1, .NET 5–10; 15 target frameworks |
| Validated native platform | Ubuntu 24.04 x86-64, ROCm 7.2.1, MIGraphX 2.15.0, gfx1100 |
| Native library | <code>/opt/rocm/lib/libmigraphx_c.so.3</code>, or the absolute path in your ROCm installation |
| Vision examples | .NET 10; HIPRTC with the matching GPU architecture, or a precompiled HIP module |

Managed framework compatibility does not provide a Windows/macOS MIGraphX runtime. Use the [AMD installation instructions](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html) and [deployment guide](docs/guides/runtime-deployment.md) to prepare the target host. Other GPU/runtime combinations are described in [platform support](docs/compatibility/platforms.md).

## 🏗️ Project Structure

~~~text
MIGraphXSharp/
├── src/
│   ├── JYPPX.ROCm.MIGraphX.CSharp.API/              # Core library
│   └── JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop/  # HIP adapter
├── samples/
│   ├── Yolo26/                                    # Five vision tasks
│   └── PaddleOcr/                                 # Text detection and recognition
├── docs/
│   ├── guides/                                    # Installation and usage
│   ├── release-notes/                             # Version history
│   └── blog/                                      # Technical articles
├── eng/                                           # Build and packaging scripts
└── MIGraphXSharp.sln
~~~

## 🔧 Build from Source

Install the SDK selected by <code>global.json</code>, PowerShell 7 and the [HipSharp source dependency](CONTRIBUTING.md). Run from the repository root:

~~~powershell
./eng/build.ps1 -Configuration Release -Version 0.0.2
./eng/pack.ps1 -Configuration Release -Version 0.0.2 -NoBuild
~~~

Builds without an explicit version use <code>0.0.0</code>. Outputs go to the sibling <code>MIGraphXSharp.Workspace/</code> directory. See [CONTRIBUTING.md](CONTRIBUTING.md) for the full build setup.

## 🤝 Contributing

Issues and pull requests are welcome. Include package versions, ROCm/MIGraphX versions, operating system, GPU and a minimal reproduction when [reporting an issue](https://github.com/guojin-yan/MIGraphX-CSharp-API/issues).

## 📄 License and Acknowledgments

The project is licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE). Thanks to [AMD MIGraphX](https://github.com/ROCm/AMDMIGraphX) and [ROCm](https://github.com/ROCm) for the native inference and GPU computing stack. Third-party libraries and model assets retain their own licenses.

Related projects: [OpenVINO C# API](https://github.com/guojin-yan/OpenVINO-CSharp-API) · [TensorRT C# API](https://github.com/guojin-yan/TensorRT-CSharp-API) · [HIP C# API](https://github.com/guojin-yan/HIP-CSharp-API).

## 📮 Contact and Sponsorship

- GitHub: [@guojin-yan](https://github.com/guojin-yan)
- NuGet: [JYPPX.ROCm.MIGraphX.CSharp.API](https://www.nuget.org/packages/JYPPX.ROCm.MIGraphX.CSharp.API/)

<p align="center">
  <img src="docs/images/readme/contact-support-en.png" width="100%" alt="Developer contacts, community links and sponsorship QR codes">
</p>

## ⚖️ Software and Usage Notice

This project uses AI-assisted development. Feedback, issue reports and contributions help improve the library. The software is provided under the Apache-2.0 license without warranties; evaluate your application's models, native runtime and deployment requirements before production use. Refer to [platform support](docs/compatibility/platforms.md) for known limitations and experimental APIs.

Copyright © 2026 Guojin Yan.
