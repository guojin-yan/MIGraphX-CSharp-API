# M4 local validation

M4 keeps the frozen M3 normalized model and generated declarations unchanged. Its separate high-level inventory maps all 192 low-level entities to 52 supported, 139 planned, and one unsupported item. Eight public ownership records cover copied metadata, owned SafeHandles, host-buffer ownership, collection snapshots, failure cleanup, Dispose, and post-Dispose behavior.

| Evidence | Local result | Scope |
| --- | --- | --- |
| `statically-verified` | Passed | Public baseline, bilingual XML, 15-TFM semantic surface, 192-item map closure, ownership manifest, package content, and M2/M3 ABI/export gates |
| `fake-native-executed` | Passed | Target/program/options/shape/argument/map/collection construction and cleanup; file/buffer parse; compile; shape snapshots; typed host copy; synchronous run; multi-item snapshots/outputs; status/null/drift/malformed injection |
| `runtime-executed` | Not added | No new official session or authorization was supplied for M4 |

The standard local sequence is:

```powershell
dotnet tool restore
./eng/generate-interop.ps1 -AcquireHeader -Verify
./eng/verify-m3-coverage.ps1
./eng/verify-m4-coverage.ps1
./eng/build.ps1 -Configuration Release
./eng/test.ps1 -Configuration Release -NoBuild
./eng/verify-m2-abi.ps1 -AcquireInputs
./eng/verify-m3-abi.ps1 -AcquireInputs
$package = ./eng/pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
./eng/verify-package.ps1 -PackagePath $package
./eng/docs.ps1 -Configuration Release -NoBuild
```

The package gate checks DLL/XML assets for all 15 TFMs, forbids generated inventories, tests, symbols, native binaries, models, and machine paths, and compiles isolated `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0` consumers against the package. Each consumer constructs and inspects M4 shape metadata.

The current official results contain the revalidated M1/M2 entries and a separate M9 option smoke at `346cdd0b01a7f8039f5deb93058928403fccc7dd`. They are not execution evidence for the broader M4 object model.

中文摘要：M4 本地门禁覆盖 192 项高低层映射、8 个 ownership 记录、15 TFM、typed host buffer、集合快照、同步 run、失败清理与包审计。没有新增官方会话，因此 M4 只能标为 `fake-native-executed`。
