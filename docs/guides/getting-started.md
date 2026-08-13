# Getting started

M0 is useful for validating the managed delivery shape, not for running MIGraphX.

## Prerequisites

- .NET SDK selected by `global.json`.
- PowerShell 7 on Windows, or Bash on Linux.
- Network access to NuGet for the first restore.

## Build and inspect

```powershell
dotnet tool restore
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
$package = .\eng\pack.ps1 -Configuration Release -Version 0.0.0 -NoBuild
.\eng\verify-package.ps1 -PackagePath $package
.\eng\docs.ps1 -Configuration Release -NoBuild
```

The smoke runner returns structured `not-probed` / `not-available` states when no native library is used. It never prints a readiness claim.

## Native boundary

The managed core carries no ROCm files. M1 will build loader and Direct P/Invoke work from the fixed official header recorded in the compatibility manifest. Until that work is implemented and tested, installing MIGraphX on the machine does not make this package functional.
