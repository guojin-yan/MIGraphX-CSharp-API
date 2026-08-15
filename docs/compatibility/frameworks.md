# Managed target frameworks

The `JYPPX.ROCm.MIGraphX.CSharp.API` core and optional `JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop` adapter build these exact assets from one central property. The core C# namespace remains `JYPPX.ROCm.MIGraphXSharp`; the adapter uses its package-matching namespace.

| Family | Target frameworks | M1 through M6 evidence |
| --- | --- | --- |
| .NET Framework | `net46`, `net461`, `net462`, `net47`, `net471`, `net472`, `net48`, `net481` | Compile and package assets |
| .NET Core | `netcoreapp3.1` | Compile, package asset, clean consumer |
| .NET | `net5.0`, `net6.0`, `net7.0`, `net8.0`, `net9.0`, `net10.0` | Compile and package assets; representative consumers for `net7.0` and `net10.0` |

The core representative clean-consumer set is `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0`. A separate `net10.0` adapter consumer compiles host async and device-input calls using only local packages. No consumer uses `ProjectReference`.

M8 schema 2 snapshots verify the exact same public contract on every listed TFM. These are managed compile/package assets, not a promise that Microsoft or this project services end-of-support .NET Framework, .NET Core, or .NET releases.

`net7.0+` selects the generated `LibraryImport` path. Older targets select the generated `DllImport` path. M3 emits both 158-EntryPoint paths from one normalized model. M4 through M6 keep one public contract across all frameworks and use internal compatibility helpers instead of removing members on old TFMs. None of the local consumer or fake-native results is official MIGraphX runtime evidence.
