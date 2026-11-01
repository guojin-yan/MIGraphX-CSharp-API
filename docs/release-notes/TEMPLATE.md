# MIGraphX C# API `<API_VERSION>` 版本说明

> 复制为对应版本文件，填写实际发布内容，删除提示文字和不适用的小节。

## 版本信息

| 项目 | 版本或内容 |
| --- | --- |
| 核心包 / HIP 适配包 | `<API_VERSION>` |
| MIGraphX | `<MIGRAPHX_VERSION>` |
| ROCm | `<ROCM_VERSION>` |
| HipSharp 依赖 | `<HIPSHARP_VERSION>` |
| 发布日期 | `<YYYY-MM-DD>` |
| 发布类型 | `<首次发布 / 功能更新 / 修复 / 兼容性更新>` |

## 更新摘要

- `<3 至 6 条面向使用者的主要变化>`

## 详细变更

### 核心 API

- `<新增功能、修复和行为变化>`

### HIP 集成

- `<异步、显存和流语义的变化；没有时删除>`

### 示例与文档

- `<案例、使用指南和 API 文档变化>`

## 兼容性与弃用项

- 破坏性变化：`<无或逐项说明>`
- 弃用 API：`<无或列出替代接口>`
- 默认行为变化：`<无或说明迁移方式>`
- 依赖变化：`<精确版本要求>`

## 升级方法

```xml
<PackageReference Include="JYPPX.ROCm.MIGraphX.CSharp.API" Version="<API_VERSION>" />
<!-- 使用 HIP 时添加；与核心包保持同版本。 -->
<PackageReference Include="JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop" Version="<API_VERSION>" />
```

1. `<依赖升级>`
2. `<代码或配置迁移；没有时明确说明>`

## 支持平台

| 组件 | 平台与版本 |
| --- | --- |
| 托管框架 | `<实际提供的目标框架>` |
| 原生运行环境 | `<已验证系统、ROCm、MIGraphX、GPU 架构>` |

## 已知限制

- `<本次变化相关限制，以及现有文档入口>`

## 验证范围

- `<面向使用者的构建、运行和兼容性结论；不放原始日志和开发过程>`

## English Summary

- Version: `<API_VERSION>`
- Main changes: `<summary>`
- Compatibility and migration: `<summary>`
- Runtime and limitations: `<summary>`
