# 发布与候选包 / Releases and candidates

本页面向维护者。候选检查生成本地材料，不会发布 NuGet 包、创建标签或部署文档。安装使用者请阅读 [Getting started](../docs/guides/getting-started.md)。

## 发布版本

所有者授权发布后，在干净且已推送的 `main` 上选择版本，例如 `0.0.2`。创建目标为精确 HEAD 的 GitHub draft release，再手动运行 `publish-release` 工作流。工作流从 NuGet.org 下载并核对下文固定哈希的 HipSharp 0.10.0。`eng/prepare-release.ps1` 负责构建、API/包检查、干净消费、漏洞查询、文档及离线分发材料，产物全部位于仓库外。

发布 job 使用 NuGet Trusted Publishing：`NuGet/login@v1` 将 GitHub OIDC 身份换取有效期一小时的临时 API Key，不再读取仓库 secret `NUGET_API_KEY`。包检查通过后发布本项目的 core/adapter 同版本包，再将 draft release 公开。HipSharp 是已有依赖，不重复推送。若发布中途失败，先检查 NuGet 是否已经接受部分包，禁止替换同版本内容后重试。

### 首次配置 Trusted Publishing

登录 NuGet.org 的 `GuojinYan` 账户，在 [Trusted Publishing](https://www.nuget.org/account/trustedpublishing) 添加 GitHub 策略：

| 字段 | 值 |
| --- | --- |
| Repository Owner | `guojin-yan` |
| Repository | `MIGraphX-CSharp-API` |
| Workflow File | `release.yml`，只填文件名 |
| Environment | 留空，当前 job 未使用 GitHub Environment |
| Package Owner | 拥有这两个包的 `GuojinYan` 账户 |
| Scope | 允许推送现有包的新版本 |
| Package pattern | `JYPPX.ROCm.MIGraphX.CSharp.API*` |

账户名称是 NuGet 用户名，不是邮箱。发布 job 已配置 `id-token: write`，无需设置新的 GitHub secret。具体机制见 [NuGet 官方文档](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)。

### 复用已准备的发布包

仅认证或发布步骤失败、准备 job 已成功时，可以在最新 `main` 上重新运行工作流，将 `prepared_run_id` 填为原运行 ID。准备 job 将跳过；发布 job 检查原运行来自本仓库 `main` 的同一发布工作流，且存在成功的准备 job，再下载其原始分发材料。包版本、源码提交和 draft release 目标必须一致，文件校验和必须通过。

```powershell
gh workflow run release.yml --ref main -f version=0.0.2 -f prepared_run_id=41267546459
```

这样可以在切换认证方式后复用已经验证的包，保留原始源码提交和 NuGet 包字节。正常准备新版本时，`prepared_run_id` 留空。只有认证配置变化时，不需要再次进行 GPU 模型验证。

## 准备候选包

按 [Contributing](../CONTRIBUTING.md) 配置 SDK、PowerShell 和 HipSharp 源码依赖。选择候选版本后，先将源码提交并推送到 `main`；最终检查要求工作区干净，且 `HEAD == origin/main == RepositoryCommit`。未指定版本时，项目仍使用未发布的 `0.0.0`。

准备 NuGet.org 上的 `JYPPX.ROCm.HIP.CSharp.API` 0.10.0 包，将路径填入以下变量。脚本核对其 SHA-256 为 `096f82ae8d8ecb1cd3ad0e1f5e213e811656cde10767a0ddf88563747e9a1558`，包内源码提交为 `53ae5a75c354d27e4df3420356351942267cc666`。适配器通过包引用重新构建，不能仅使用解决方案的源码引用输出。

公开的 HipSharp 0.9.1 缺少适配器需要的内部 stream lease 接口，不能互换。当前适配器精确依赖公开 0.10.0；0.0.2 的设备输出、同流后处理和提前释放已在该依赖上执行真实 GPU 检查。历史检查材料保留各自的依赖身份，不混用源码候选与公开包结果。

```powershell
$version = '0.0.2-rc.1' # Example; choose the intended candidate version.
$commit = (git rev-parse HEAD).Trim()
$hipPackage = '/absolute/path/JYPPX.ROCm.HIP.CSharp.API.0.10.0.nupkg'
$core = ./eng/pack.ps1 -Configuration Release -Version $version
$adapter = ./eng/pack-adapter.ps1 -Configuration Release -Version $version -HipSharpPackagePath $hipPackage -NoBuild | Select-Object -Last 1
$candidate = @{
    CorePackagePath = $core; AdapterPackagePath = $adapter
    Version = $version; RepositoryCommit = $commit
}
$index = ./eng/verify-release-candidate.ps1 @candidate -HipSharpPackagePath $hipPackage | Select-Object -Last 1
```

此命令检查包内容和独立消费项目，核对实际解析的三个依赖版本，再查询含传递依赖的 NuGet 漏洞信息。查询失败、项目缺失、报告存在问题或漏洞时，不生成通过材料。需要可用的包源和漏洞信息源；禁用 NuGet audit 的离线 restore 不能代替这项检查。这些托管包检查无需 AMD GPU 或云端主机。

## 离线复核已有材料

输出默认位于仓库外的 `../MIGraphXSharp.Workspace/artifacts/release-evidence/<version>/<commit>/`。保留两个原始 `.nupkg`、索引和索引引用的全部四个 JSON 文件：包清单、CycloneDX SBOM、provenance、漏洞查询摘要。整个目录可复制到其他机器；文件名和包字节必须保持不变。

```powershell
./eng/verify-release-evidence.ps1 @candidate -IndexPath $index
```

离线检查重新读取包中的版本和提交，核对包与文件哈希、固定 HipSharp 依赖、SBOM 依赖关系、provenance subject 和漏洞查询摘要之间的一致性。它只读取索引明确引用的同目录文件，也支持旧材料的文件名。复核不会重新构建、查询网络或执行原生 runtime。

修改发布检查脚本时，可对已有有效候选材料执行有针对性的回归检查：

```powershell
./eng/test-release-evidence.ps1 @candidate -IndexPath $index
```

回归检查使用独立的仓库外副本，覆盖提交/版本混用、缺失文件、重复角色、路径越界、重新计算索引哈希后仍存在的清单/SBOM/provenance 矛盾，以及失败的漏洞查询。输入包和记录不会被修改。

本地材料未签名，哈希和离线复核证明文件间的一致性，不能认证发布者身份或保证漏洞信息始终最新。它们也不提升原生平台支持等级。真实 runtime 支持范围以 [平台限制](../docs/compatibility/platforms.md) 为准；包发布、标签及站点部署需要单独的所有者授权。

These commands prepare and review managed package candidates without a GPU. Offline review checks source/package identities and evidence consistency; it does not refresh advisory data, authenticate unsigned records, qualify native runtime support, or publish anything.
