# M3 local validation status

| Evidence | Result | Boundary |
| --- | --- | --- |
| `statically-verified` | passed locally | Fixed header hash/length; 159 functions, 2 enums, 25 handles, 6 callbacks; 192 mutually exclusive classifications; 158 matching managed EntryPoints per path. |
| `statically-verified` | passed locally | Official ELF contains all 159 header functions, plus one separately classified private test export; enum values, one-byte C `bool`, 64-bit `size_t`, handle pointers, and callback pointers passed the x64 C probe. |
| `fake-native-executed` | passed locally | Cdecl callback retention and exception containment, bool, size_t, explicit UTF-8 length, borrowed pointer identity, out handle cleanup, and array-length behavior executed against the minimal test substitute. |
| `runtime-executed` | not added by M3 | No M3-only declaration ran against official MIGraphX; the four existing M1/M2 records remain bounded to `f1a11cfd1701a041cee29188f7600c85b34ae260`. |

Run the local gates with:

```powershell
dotnet tool restore
./eng/generate-interop.ps1 -AcquireHeader -Verify
./eng/verify-m3-coverage.ps1
./eng/build.ps1 -Configuration Release
./eng/test.ps1 -Configuration Release -NoBuild
./eng/verify-m2-abi.ps1 -AcquireInputs
./eng/verify-m3-abi.ps1 -AcquireInputs
$package = ./eng/pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
./eng/verify-package.ps1 -PackagePath $package
./eng/docs.ps1 -Configuration Release -NoBuild
```

`verify-m3-abi.ps1` writes a machine-readable local result under ignored `artifacts/m3-abi/`; it never copies the official ELF into source or the package. `-AcquireInputs` reuses a trusted cache when present, verifies every fixed hash, and keeps TLS validation enabled for any missing download.

Generation canaries cover repeated byte-identical output, fixed-input hash and length drift, byte-drift detection, callback/bool/size_t parsing, incomplete callback/unsupported classification, and rejected-input non-pollution. The standard build, test, and docs entry points all execute the closed-coverage gate.

No cloud connection was made for M3 and prior authorization was not reused. A future official runtime run requires a new explicit authorization and a separately identified pushed commit.

中文说明：M3 本地门禁证明固定头、normalized model、双声明路径与官方 ELF 的静态闭合，并用最小 fake-native 实调关键 ABI 模式。M3 没有新增官方 runtime 证据，也没有复用历史云端授权。
