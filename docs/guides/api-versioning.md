# API compatibility and versioning

The public contract is the schema 2 snapshot under `compatibility/`. It includes signatures, defaults, generic constraints, nullable metadata, assembly/package identity, and identical availability across all 15 managed TFMs. A public addition, removal, rename, accessibility change, default change, or nullable change requires review and a deliberate baseline update.

Build a candidate with an explicit prerelease version and source commit:

```powershell
$commit = git rev-parse HEAD
.\eng\build.ps1 -Configuration Release -Version 0.9.0-rc.1 -RepositoryCommit $commit
.\eng\verify-public-api.ps1 -Configuration Release -Version 0.9.0-rc.1 -RepositoryCommit $commit -SkipToolBuild
```

Do not edit the default `0.0.0` to `1.0.0` during prerelease work. Development stays on `0.x.x`; intentional interface additions update the compatibility snapshots through review. A final version bump requires completed Windows runtime validation, `release-ready` evidence, and separate Owner approval, followed by a complete rebuild and new package hashes.

The supported native baseline is separate: Ubuntu 24.04 amd64, ROCm 7.2.1, and MIGraphX `2.15.0.70201-81~24.04`. Validate any native upgrade independently. Managed TFM assets, especially end-of-support .NET versions, do not imply runtime servicing from Microsoft or this project.

中文摘要：schema 2 快照是公开契约基线门禁；预发布构建显式传入版本和源码 SHA。托管版本不会自动改变系统 ROCm/MIGraphX；开发保持 `0.x.x`，最终 `1.0.0` bump 还需要 Windows 实机验证、单独冻结授权并重建全部证据。
