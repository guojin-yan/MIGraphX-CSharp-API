[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$sourceDirectory = Join-Path $root 'native\fake-migraphx'
$outputDirectory = Join-Path $root "artifacts\fake-native\$Configuration"
$hipSource = Join-Path $root 'native\fake-hip\fake_hip.c'
$hipOutputDirectory = Join-Path $root "artifacts\fake-hip\$Configuration"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $hipOutputDirectory | Out-Null

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio C++ tools were not found.' }
    $installation = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
    if (-not $installation) { throw 'Visual Studio C++ tools were not found.' }
    $vcvars = Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'
    $originalEnvironment = @{}
    Get-ChildItem Env: | ForEach-Object { $originalEnvironment[$_.Name] = $_.Value }
    $environmentLines = & $env:ComSpec /d /s /c "`"$vcvars`" >nul && set"
    if ($LASTEXITCODE -ne 0) { throw 'Failed to initialize the Visual Studio C++ environment.' }
    foreach ($line in $environmentLines) {
        $separator = $line.IndexOf('=')
        if ($separator -gt 0) {
            [Environment]::SetEnvironmentVariable($line.Substring(0, $separator), $line.Substring($separator + 1), 'Process')
        }
    }
    $compilerPath = (Get-Command cl.exe -ErrorAction Stop).Source
    try {
        Push-Location $outputDirectory
        try {
            foreach ($item in @(
                @{ Source = 'fake_migraphx.c'; Output = 'migraphx_c.dll' },
                @{ Source = 'fake_migraphx.c'; Output = 'migraphx_c_m10_missing.dll'; Defines = @('/DFAKE_DISABLE_M10') },
                @{ Source = 'missing_export.c'; Output = 'migraphx_c_missing_export.dll' },
                @{ Source = 'm1_only.c'; Output = 'migraphx_c_m1_only.dll' }
            )) {
                $source = Join-Path $sourceDirectory $item.Source
                $output = Join-Path $outputDirectory $item.Output
                $compileArguments = @('/nologo', '/LD', '/W4', '/WX', '/O2')
                if ($item.ContainsKey('Defines')) { $compileArguments += $item.Defines }
                $compileArguments += @($source, '/link', "/OUT:$output")
                & $compilerPath @compileArguments
                if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) {
                    throw "Failed to build fake native library: $($item.Output)"
                }
            }
            $hipOutput = Join-Path $hipOutputDirectory 'amdhip64.dll'
            & $compilerPath /nologo /LD /W4 /WX /O2 $hipSource /link "/OUT:$hipOutput"
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $hipOutput)) {
                throw 'Failed to build fake HIP native library.'
            }
        }
        finally { Pop-Location }
    }
    finally {
        Get-ChildItem Env: | Where-Object { -not $originalEnvironment.ContainsKey($_.Name) } | ForEach-Object {
            [Environment]::SetEnvironmentVariable($_.Name, $null, 'Process')
        }
        foreach ($entry in $originalEnvironment.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
        }
    }
    $compiler = 'MSVC x64'
}
else {
    $compilerCommand = Get-Command cc -ErrorAction SilentlyContinue
    if (-not $compilerCommand) { $compilerCommand = Get-Command gcc -ErrorAction SilentlyContinue }
    if (-not $compilerCommand) { throw 'A C compiler (cc or gcc) is required to build fake-native.' }
    & $compilerCommand.Source -shared -fPIC -Wall -Wextra -Werror -O2 (Join-Path $sourceDirectory 'fake_migraphx.c') -o (Join-Path $outputDirectory 'libmigraphx_c.so')
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build fake native library.' }
    & $compilerCommand.Source -shared -fPIC -Wall -Wextra -Werror -O2 -DFAKE_DISABLE_M10 (Join-Path $sourceDirectory 'fake_migraphx.c') -o (Join-Path $outputDirectory 'libmigraphx_c_m10_missing.so')
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build M10-missing fake native library.' }
    & $compilerCommand.Source -shared -fPIC -Wall -Wextra -Werror -O2 (Join-Path $sourceDirectory 'missing_export.c') -o (Join-Path $outputDirectory 'libmigraphx_c_missing_export.so')
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build missing-export fake native library.' }
    & $compilerCommand.Source -shared -fPIC -Wall -Wextra -Werror -O2 (Join-Path $sourceDirectory 'm1_only.c') -o (Join-Path $outputDirectory 'libmigraphx_c_m1_only.so')
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build M1-only fake native library.' }
    & $compilerCommand.Source -shared -fPIC -Wall -Wextra -Werror -O2 $hipSource -o (Join-Path $hipOutputDirectory 'libamdhip64.so')
    if ($LASTEXITCODE -ne 0) { throw 'Failed to build fake HIP native library.' }
    $compiler = $compilerCommand.Source
}

[PSCustomObject]@{
    Evidence = 'fake-native-executed-test-substitute'
    Compiler = $compiler
    Directory = $outputDirectory
    HipDirectory = $hipOutputDirectory
}
