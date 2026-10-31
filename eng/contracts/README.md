# 工具输入契约 / Tooling contracts

本目录只保存当前维护工具需要的输入和基线，不保存验证输出、阶段计划或历史执行状态。使用 NuGet 包的应用不需要读取这些文件。

| 文件 | 使用者与用途 |
| --- | --- |
| `binding-overrides.json`、`binding-classifications.json`、`unsupported-api.json` | `generate-interop.ps1` 使用的参数投影、回调保留规则与变参排除规则 |
| `normalized-api.json` | 冻结 C 头文件的声明模型，供绑定契约和导出检查使用；由生成器维护 |
| `native-abi.json` | `verify-native-abi.ps1` 使用的原生 ABI 和导出基线 |
| `public-api.txt`、`hip-interop-public-api.txt` | `verify-public-api.ps1` 使用的公共 API 快照 |
| `system-native-source-lock.json` | `prepare-runtime.ps1` 使用的 AMD 原生依赖来源、版本和哈希 |
| `test-native-exports.json` | `verify-test-native-exports.ps1` 使用的替代原生库导出契约 |
| `package-distribution.json` | 托管包分发策略 |
| `schemas/` | 上述结构化输入的格式定义 |

修改绑定时，通过 `../generate-interop.ps1` 更新声明模型和生成源码。构建输出、下载缓存及检查报告写入仓库外的工作目录。平台支持条件见[使用文档](../../docs/compatibility/platforms.md)。

These are maintained inputs to generation, compatibility and packaging tools. Generated reports, logs, session permissions and development history belong outside the source checkout.
