# Managed target frameworks

The `JYPPX.ROCm.MIGraphX.CSharp.API` core project and assembly build these exact assets from one central property. Its C# namespace remains `JYPPX.ROCm.MIGraphXSharp`.

| Family | Target frameworks | M1/M2/M3 evidence |
| --- | --- | --- |
| .NET Framework | `net46`, `net461`, `net462`, `net47`, `net471`, `net472`, `net48`, `net481` | Compile and package assets |
| .NET Core | `netcoreapp3.1` | Compile, package asset, clean consumer |
| .NET | `net5.0`, `net6.0`, `net7.0`, `net8.0`, `net9.0`, `net10.0` | Compile and package assets; representative consumers for `net7.0` and `net10.0` |

The representative clean-consumer set is `net46`, `netcoreapp3.1`, `net7.0`, and `net10.0`. These consumers install the actual local `.nupkg`; they do not use `ProjectReference`.

`net7.0+` selects the generated `LibraryImport` path. Older targets select the generated `DllImport` path. M3 emits both 158-EntryPoint paths from one normalized model. The representative set executes the M1 lifecycle and M2 restricted ONNX workflow against the local fake-native test substitute; isolated M3 ABI patterns execute in the net10.0 unit suite. None of that is official MIGraphX runtime evidence.
