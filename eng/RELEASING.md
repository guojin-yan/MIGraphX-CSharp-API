# 发布与候选包 / Releases and candidates

本页面向维护者。候选检查生成本地材料，不会发布 NuGet 包、创建标签或部署文档。安装使用者请阅读 [Getting started](../docs/guides/getting-started.md)。

## 发布版本

所有者授权发布后，在干净且已推送的 `main` 上选择版本，例如 `0.0.1`。创建目标为精确 HEAD 的 GitHub draft release，再手动运行 `publish-release` 工作流。工作流从 NuGet.org 下载并核对下文固定哈希的 HipSharp 0.10.0。`eng/prepare-release.ps1` 负责构建、API/包检查、干净消费、漏洞查询、文档及离线分发材料，产物全部位于仓库外。

发布 job 只使用仓库 secret `NUGET_API_KEY`，不会读取或导出其内容。包检查通过后发布本项目的 core/adapter 同版本包，再将 draft release 公开。HipSharp 是已有依赖，不重复推送。若发布中途失败，先检查 NuGet 是否已经接受部分包，禁止替换同版本内容后重试。

## 准备候选包

按 [Contributing](../CONTRIBUTING.md) 配置 SDK、PowerShell 和 HipSharp 源码依赖。选择候选版本后，先将源码提交并推送到 `main`；最终检查要求工作区干净，且 `HEAD == origin/main == RepositoryCommit`。未指定版本时，项目仍使用未发布的 `0.0.0`。

准备 NuGet.org 上的 `JYPPX.ROCm.HIP.CSharp.API` 0.10.0 包，将路径填入以下变量。脚本核对其 SHA-256 为 `096f82ae8d8ecb1cd3ad0e1f5e213e811656cde10767a0ddf88563747e9a1558`，包内源码提交为 `53ae5a75c354d27e4df3420356351942267cc666`。适配器通过包引用重新构建，不能仅使用解决方案的源码引用输出。

此前 runtime 检查使用的本地 HipSharp 0.9.1 候选包含内部 stream lease 接口；公开的 0.9.1 缺少该接口，不能互换。公开 0.10.0 含相同的接口和相关流/内存实现。历史 runtime 材料保留原依赖身份；此次发布执行真实公开依赖的托管编译与干净消费检查，不宣称重新进行了 GPU 验证。

```powershell
$version = '0.0.1-rc.1' # Example; choose the intended candidate version.
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
