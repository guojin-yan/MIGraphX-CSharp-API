# 构建与维护命令 / Build and maintenance commands

从源码构建时在仓库根目录运行命令。使用 NuGet 包的应用无需执行这些脚本。SDK、PowerShell 和 HipSharp 源码依赖见 [Contributing](../CONTRIBUTING.md)。

| 常用命令 | 用途 |
| --- | --- |
| `./eng/build.ps1 -Configuration Release` | 编译解决方案并检查绑定、公共 API 和目录边界 |
| `./eng/test.ps1 -Configuration Release -NoBuild` | 对已有构建执行本地测试和替代原生库检查 |
| `./eng/pack.ps1 -Configuration Release -Version 0.0.0` | 生成核心 NuGet 包 |
| `./eng/pack-adapter.ps1 -Version 0.0.0 -HipSharpPackagePath <path>` | 生成 HIP 适配包，要求匹配的核心包和 HipSharp 包 |
| `./eng/docs.ps1 -Configuration Release -NoBuild` | 生成使用文档和 API 参考站点 |

`build.sh` 在 Bash 环境依次运行构建、本地测试和文档生成。所有输出默认写入仓库外的 `../MIGraphXSharp.Workspace/`，可通过 `MIGRAPHX_WORKSPACE_ROOT` 指定其他仓库外绝对目录。

其余脚本面向维护人员：

| 脚本 | 用途 |
| --- | --- |
| `generate-interop.ps1` | 根据冻结头文件生成绑定，`-Verify` 检查字节一致性 |
| `verify-binding-contract.ps1` | 检查绑定声明、回调和分类是否与输入契约一致 |
| `verify-native-abi.ps1` | 用指定头文件和官方 ELF 静态检查完整 ABI；不执行官方 runtime |
| `verify-hipsharp-source.ps1` | 检查 HIP 适配器的源码依赖及内部访问契约 |
| `verify-public-api.ps1` | 比较各目标框架的公共 API 快照 |
| `build-fake-native.ps1`、`verify-test-native-exports.ps1`、`test-interop-paths.ps1` | 构建替代库、检查导出并执行代表性互操作测试 |
| `prepare-runtime.ps1`、`test-runtime-source.ps1` | 获取锁定的原生来源审计输入并检查来源处理逻辑 |
| `prepare-hipsharp-baseline.ps1` | 准备固定 HipSharp 源码依赖 |
| `verify-package.ps1`、`verify-adapter-package.ps1` | 检查包内容及独立消费项目 |
| `new-release-evidence.ps1`、`verify-release-evidence.ps1`、`verify-release-candidate.ps1` | 生成和检查包身份、SBOM、provenance 及发布候选材料 |
| `test-release-evidence.ps1` | 对有效材料的独立副本执行离线回归，拒绝包身份混用和材料内容矛盾 |
| `verify-repository-layout.ps1` | 拒绝产物、开发记录和按开发阶段命名的工程脚本进入仓库 |

`common.ps1`、`*.psm1`、`workspace.sh` 和 `include/` 为脚本提供公共函数或编译输入。[contracts/](contracts/README.md) 只保留当前工具使用的绑定、API 与依赖基线。测试模型生成器位于 [tests/fixtures](../tests/fixtures/README.md)。发布检查不会自动发布包、创建标签或部署站点。

候选包准备、材料保存和离线复核示例见 [发布候选包](RELEASING.md)。

Run these commands from the repository root when building from source. Application users who consume the NuGet packages do not need these scripts. Active commands are organized by function. Generated output stays outside the source checkout.
