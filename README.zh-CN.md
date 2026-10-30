# MIGraphXSharp

[English](README.md)

MIGraphXSharp 为 AMD MIGraphX 提供 .NET 绑定和托管资源封装，可用于加载、编译 ONNX 模型、执行推理、读取形状与解析器能力，以及集成 HipSharp 流。

项目尚未发布，默认包版本为 `0.0.0`。原生基线为 Ubuntu 24.04 x86-64 上的 ROCm 7.2.1 / MIGraphX `2.15.0.70201-81~24.04`。选择功能前请阅读[平台支持与限制](docs/compatibility/platforms.md)。

## 软件包与环境

| 软件包 | 用途 |
| --- | --- |
| `JYPPX.ROCm.MIGraphX.CSharp.API` | 核心绑定、同步推理、托管对象和诊断 |
| `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` | 可选的 HipSharp 流与设备输入集成 |

核心命名空间为 `JYPPX.ROCm.MIGraphXSharp`。两个包均提供从 `net46` 到 `net10.0` 的 [15 个目标框架](docs/compatibility/frameworks.md)。提供托管框架程序集不代表对应平台具有官方原生运行库。

请通过 [AMD 官方系统仓库](https://rocm.docs.amd.com/projects/install-on-linux/en/docs-7.2.1/install/quick-start.html)安装 MIGraphX 和 ROCm。本项目采用 `system-native` 部署，只分发托管程序集，不提供原生 Runtime NuGet 包。

## 快速开始

安装 `global.json` 指定的 .NET SDK 和 PowerShell 7，配置 [HipSharp 源码依赖](CONTRIBUTING.md)后构建本地包：

```powershell
.\eng\pack.ps1 -Configuration Release -Version 0.0.0
```

包输出到 `../MIGraphXSharp.Workspace/artifacts/packages/`。构建完整解决方案时还需配置 [HipSharp 源码依赖](CONTRIBUTING.md)。

对于只有一个静态、标准布局 float32 输入和输出的模型：

```csharp
using JYPPX.ROCm.MIGraphXSharp;

var result = MIGraphXOnnxWorkflow.RunFile(
    "/opt/rocm-7.2.1/lib/libmigraphx_c.so.3",
    "/absolute/path/to/model.onnx",
    new[] { 1f, 2f, 3f, 4f });
Console.WriteLine(string.Join(",", result.Output));
```

路径必须是绝对路径，输入长度必须与模型一致。此入口启用 GPU offload-copy，同步执行并返回独立的主机输出副本。库诊断见[入门指南](docs/guides/getting-started.md)，多输入、多输出组合见[托管对象用法](docs/guides/managed-objects.md)。

## 功能与边界

API 包含显式资源所有权、不可变形状快照、类型化主机输入、ONNX 选项、动态维度、程序持久化和缓存、图构建、类型化 operation 属性、TensorFlow 解析、量化与实验性自定义算子。

真实运行时验证尚未全部完成。高级 API 与 HIP 异步、设备缓冲区路径仍有待验证的运行语义；本地替代库测试不能证明 GPU 支持或性能。通用 C 可变参数，以及缺少原生销毁契约的独立 module 所有者不在公开封装范围内。具体边界见[平台支持说明](docs/compatibility/platforms.md)。

## 使用文档

- [入门指南](docs/guides/getting-started.md)
- [托管对象与资源所有权](docs/guides/managed-objects.md)
- [动态形状、持久化与缓存](docs/guides/dynamic-shapes-cache.md)
- [类型化 operation 属性](docs/guides/operation-attributes.md)
- [HIP 异步集成](docs/guides/hip-interop.md)
- [图、TensorFlow、量化与实验性 API](docs/guides/advanced-apis.md)
- [部署与故障排查](docs/guides/runtime-deployment.md)
- [框架兼容性](docs/compatibility/frameworks.md)与 [API 版本规则](docs/guides/api-versioning.md)
- [API 参考](docs/api/index.md)

## 仓库结构

`src/` 存放库源码；`samples/` 存放示例；`docs/` 存放使用文档；`tests/` 和 `native/` 存放测试源码；`eng/` 和 `tools/` 存放可复现的构建、诊断脚本；`compatibility/` 存放生成器输入和 API、支持范围契约。

构建产物、自动生成的 API 页面、缓存与诊断结果默认输出到仓库旁的 `MIGraphXSharp.Workspace/`。可通过 `MIGRAPHX_WORKSPACE_ROOT` 指定仓库外的绝对目录。开发计划、会话记录和文章草稿应存放在程序仓库之外，详见[贡献指南](CONTRIBUTING.md)。

## 许可证

Copyright 2026 Guojin Yan。项目使用 [Apache-2.0](LICENSE) 许可证，另见 [NOTICE](NOTICE)。AMD、ROCm、MIGraphX 组件遵循各自许可证。
