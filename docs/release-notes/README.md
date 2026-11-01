# 版本说明 / Release Notes

本目录集中保存 MIGraphX C# API 的正式版本记录，从 0.0.1 起按版本维护。根目录 README 只保留当前版本摘要；完整变更、兼容性、升级方法、性能参考和已知限制见对应版本文件。

This directory contains the published MIGraphX C# API release history. The root README contains only the current release summary; per-version notes describe changes, compatibility, migration and known limitations.

## 版本索引 / Release Index

| API 版本 | MIGraphX | ROCm | HipSharp 依赖 | 发布日期 | 类型 | 详细说明 |
| --- | --- | --- | --- | --- | --- | --- |
| 0.0.2 | 2.15.0 | 7.2.1 | 0.10.0 | 2026-11-01 | HIP 流水线与 OCR 加速 / Feature update | [查看说明](0.0.2.md) |
| 0.0.1 | 2.15.0 | 7.2.1 | 0.10.0 | 2026-10-31 | 首次发布 / Initial release | [查看说明](0.0.1.md) |

表中的 MIGraphX、ROCm 是已验证的原生版本组合，需由使用者另行安装。HipSharp 为可选 HIP 适配器的精确包依赖，只使用核心包的应用无需安装。核心包与适配包始终使用相同版本号。

The native versions above identify the validated runtime combination, installed separately by the consumer. HipSharp is an exact dependency of the optional HIP adapter. The core package and adapter use matching versions.

## 记录格式 / Note Format

后续发布可使用[版本说明模板](TEMPLATE.md)。每个版本记录包括：版本信息、更新摘要、详细变更、兼容性与弃用项、升级方法、支持平台、已知限制和验证范围。README 与本索引中的版本号、包名和链接应与单版本文件保持一致。

Use the [release note template](TEMPLATE.md) for future versions. Keep README summaries, this index and per-version notes consistent. Record user-facing changes here; build logs, cloud session details and development notes stay outside the repository.
