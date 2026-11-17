# 构建与维护命令 / Build and maintenance commands

从源码构建时在仓库根目录运行命令。使用 NuGet 包的应用无需执行这些脚本。SDK、PowerShell 和 HipSharp 源码依赖见 [Contributing](../CONTRIBUTING.md)。

| 常用命令 | 用途 |
| --- | --- |
| `./eng/prepare-development.ps1 -HipSharpSourceRepository <path>` | 在外部工作区准备兼容的 HipSharp 开发依赖，供 VS 与 dotnet 自动使用 |
| `./eng/test-runtime.ps1 -MIGraphXRoot <path> -HipRoot <path>` | 在 Windows / Linux 对真实 MIGraphX 2.15 或 2.18 执行 GPU、量化、动态缓存与 HIP 验证 |
| `./eng/build.ps1 -Configuration Release` | 编译解决方案并检查绑定、公共 API 和目录边界 |
| `./eng/test.ps1 -Configuration Release -NoBuild` | 对已有构建执行本地测试和替代原生库检查 |
| `./eng/pack.ps1 -Configuration Release -Version 1.0.0` | 生成核心 NuGet 包 |
| `./eng/pack-adapter.ps1 -Version 1.0.0 -HipSharpPackagePath <path>` | 生成 HIP 适配包，要求匹配的核心包和 HipSharp 包 |
| `./eng/pack.ps1 -Runtime -RuntimePlatform <platform> -NativeSourceArchive <zip> -Version 1.0.0` | 从锁定原生输入生成平台 runtime 包 |
| `./eng/docs.ps1 -Configuration Release -NoBuild` | 生成使用文档和 API 参考站点 |

`build.sh` 在 Bash 环境依次运行构建、本地测试和文档生成。所有输出默认写入仓库外的 `../MIGraphXSharp.Workspace/`，可通过 `MIGRAPHX_WORKSPACE_ROOT` 指定其他仓库外绝对目录。

`test-runtime.ps1` 将常规、FP16、BF16、INT8、FP8、GPU 回调及资源循环检查分为七个子进程。GPU 回调需要配套的 HIPRTC，默认测试架构为 `gfx1100`；其他 GPU 可通过 `MIGRAPHX_TEST_GPU_ARCHITECTURE` 指定并重新验证。资源循环包含四实例共 1,000 次设备推理和 50 轮创建/编译/提前释放，结果与显存采样随日志保存。

每组默认最多运行 900 秒，可通过 `-GroupTimeoutSeconds` 调整。超时会终止该子进程及其派生进程，保留日志和最后执行项，并继续其他组；超时和截断的 JSON 报告都计为失败。`test-process-runner.ps1` 无需 GPU，验证超时清理、非零退出码和标准输出/错误捕获。

GitHub 的 managed-build 在 main 推送、PR 和手动触发时执行 Windows/Linux 托管构建、替代库测试、包消费及文档检查。旧 TFM 的 smoke 程序在 CI 中允许运行于已安装的新 runtime，真实旧 runtime 与 GPU 支持仍需单独验证。发布和 Pages 部署保留手动入口。

其余脚本面向维护人员：

| 脚本 | 用途 |
| --- | --- |
| `generate-interop.ps1` | 根据冻结头文件生成绑定，`-Verify` 检查字节一致性 |
| `generate-extensions.ps1` | 根据独立固定的 2.18 头文件生成可选绑定，`-Verify` 检查字节一致性 |
| `verify-binding-contract.ps1` | 检查绑定声明、回调和分类是否与输入契约一致 |
| `verify-native-abi.ps1` | 用指定头文件和官方 ELF 静态检查完整 ABI；不执行官方 runtime |
| `verify-hipsharp-source.ps1` | 检查 HIP 适配器的源码依赖及内部访问契约 |
| `verify-public-api.ps1` | 比较各目标框架的公共 API 快照 |
| `build-fake-native.ps1`、`verify-test-native-exports.ps1`、`test-interop-paths.ps1` | 构建替代库、检查导出并执行代表性互操作测试 |
| `prepare-runtime.ps1`、`test-runtime-source.ps1` | 获取锁定的原生来源审计输入并检查来源处理逻辑 |
| `prepare-hipsharp-baseline.ps1` | 准备固定 HipSharp 源码依赖 |
| `verify-package.ps1`、`verify-adapter-package.ps1` | 检查包内容及独立消费项目 |
| `pack-runtime.py`、`verify-runtime-consumer.ps1` | 检查原生来源、精确文件哈希及 runtime 包独立发现和参考推理 |
| `new-release-evidence.ps1`、`verify-release-evidence.ps1`、`verify-release-candidate.ps1` | 生成和检查包身份、SBOM、provenance 及发布候选材料 |
| `test-release-evidence.ps1` | 对有效材料的独立副本执行离线回归，拒绝包身份混用和材料内容矛盾 |
| `verify-repository-layout.ps1` | 拒绝产物、开发记录和按开发阶段命名的工程脚本进入仓库 |

`common.ps1`、`*.psm1`、`workspace.sh` 和 `include/` 为脚本提供公共函数或编译输入。[contracts/](contracts/README.md) 只保留当前工具使用的绑定、API 与依赖基线。测试模型生成器位于 [tests/fixtures](../tests/fixtures/README.md)。发布检查不会自动发布包、创建标签或部署站点。

候选包准备、材料保存和离线复核示例见 [发布候选包](RELEASING.md)。

Run these commands from the repository root when building from source. Application users who consume the NuGet packages do not need these scripts. Active commands are organized by function. Generated output stays outside the source checkout.
