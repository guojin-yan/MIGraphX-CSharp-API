[CmdletBinding()]
param([Parameter(Mandatory)][string] $HeaderPath, [switch] $Verify)
. (Join-Path $PSScriptRoot 'common.ps1')
$ErrorActionPreference = 'Stop'
$root = Get-RepositoryRoot
$HeaderPath = (Resolve-Path -LiteralPath $HeaderPath).Path
$outputRoot = Join-Path (Get-ArtifactsRoot) 'binding-generator/extensions-2.18'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$preprocessed = Join-Path $outputRoot 'migraphx.preprocessed.c'
$includeRoot = Join-Path $root 'eng/include/preprocess'
$nativeIncludeRoot = Split-Path (Split-Path $HeaderPath -Parent) -Parent
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = (& $vswhere -all -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    if (-not $installation) { throw 'Visual Studio C++ tools are required.' }
    $toolset = Get-ChildItem -LiteralPath (Join-Path $installation 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
    & (Join-Path $toolset.FullName 'bin/Hostx64/x64/cl.exe') /nologo /P /EP /TC /X "/I$includeRoot" "/I$nativeIncludeRoot" "/Fi$preprocessed" $HeaderPath
} else {
    & cc -E -P -nostdinc -I $includeRoot -I $nativeIncludeRoot -x c $HeaderPath -o $preprocessed
}
if ($LASTEXITCODE -ne 0) { throw 'Header preprocessing failed.' }
& dotnet run --project (Join-Path $root 'tools/BindingGenerator/BindingGenerator.csproj') -c Release -- --extensions-header $HeaderPath --preprocessed $preprocessed --output $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Extension generation failed.' }
$outputs = @{
    'NativeMethods.Extensions218.g.cs' = 'src/JYPPX.ROCm.MIGraphX.CSharp.API/Interop/NativeMethods.Extensions218.g.cs'
    'native-extensions-2.18.json' = 'eng/contracts/native-extensions-2.18.json'
}
foreach ($name in $outputs.Keys) {
    $generated = Join-Path $outputRoot $name
    $tracked = Join-Path $root $outputs[$name]
    if ($Verify) {
        if (-not (Test-Path -LiteralPath $tracked) -or (Get-FileHash -LiteralPath $generated).Hash -ne (Get-FileHash -LiteralPath $tracked).Hash) { throw "Extension binding drift: $name" }
    } else { Copy-Item -LiteralPath $generated -Destination $tracked -Force }
}
