# Managed target frameworks

The core and optional HIP adapter provide the same managed public API across these 15 targets:

| Family | Target frameworks |
| --- | --- |
| .NET Framework | `net46`, `net461`, `net462`, `net47`, `net471`, `net472`, `net48`, `net481` |
| .NET Core | `netcoreapp3.1` |
| .NET | `net5.0`, `net6.0`, `net7.0`, `net8.0`, `net9.0`, `net10.0` |

.NET 7 and later select generated `LibraryImport` bindings; older targets use generated `DllImport` bindings. Both are generated from the same frozen native API model.

Framework assets describe managed compatibility. They do not provide an official MIGraphX runtime for every OS and do not extend servicing for .NET releases that have reached end of support. For new applications, select a currently serviced .NET version compatible with your deployment host.

The core namespace is `JYPPX.ROCm.MIGraphXSharp`. The adapter namespace is `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop`. Native deployment requirements and incomplete runtime paths are listed in [platform support](platforms.md).

中文提示：15 个 TFM 共享公开 API；托管程序集可编译不代表 Windows 等平台具有官方 MIGraphX 原生运行库。
