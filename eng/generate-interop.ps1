[CmdletBinding()]
param(
    [string] $HeaderPath,
    [switch] $AcquireHeader,
    [switch] $Verify
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$manifestPath = Join-Path $root 'compatibility\m2-binding-subset.json'
$classificationPath = Join-Path $root 'compatibility\m3-handwritten-overrides.json'
$unsupportedPath = Join-Path $root 'compatibility\m3-unsupported.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

if ($AcquireHeader) {
    if (-not $HeaderPath) {
        $HeaderPath = Join-Path $root '.cache\m1\migraphx.h'
    }
    if (-not (Test-Path -LiteralPath $HeaderPath)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $HeaderPath) | Out-Null
        Invoke-WebRequest -Uri $manifest.source.headerUrl -OutFile $HeaderPath
    }
}

if (-not $HeaderPath) {
    throw 'Specify -HeaderPath or use -AcquireHeader.'
}
$HeaderPath = (Resolve-Path -LiteralPath $HeaderPath).Path
$actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $HeaderPath).Hash.ToLowerInvariant()
if ($actualHash -ne $manifest.source.headerSha256) {
    throw "Frozen header SHA-256 mismatch. Expected $($manifest.source.headerSha256), got $actualHash."
}
if ((Get-Item -LiteralPath $HeaderPath).Length -ne $manifest.source.headerByteLength) {
    throw 'Frozen header byte length does not match the manifest.'
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryBase ("migraphxsharp-m3-" + [Guid]::NewGuid().ToString('N'))
$preprocessedPath = Join-Path $temporaryRoot 'migraphx.preprocessed.c'
$outputRoot = Join-Path $temporaryRoot 'output'
$includeRoot = Join-Path $root 'eng\m3-include'

try {
    New-Item -ItemType Directory -Force -Path $temporaryRoot, $outputRoot | Out-Null
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio C++ tools were not found for M3 preprocessing.' }
        $installation = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
        if (-not $installation) { throw 'Visual Studio C++ tools were not found for M3 preprocessing.' }
        $toolsVersion = Get-ChildItem -LiteralPath (Join-Path $installation 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
        $compiler = Join-Path $toolsVersion.FullName 'bin\Hostx64\x64\cl.exe'
        $compilerOutput = @(& $compiler /nologo /P /EP /TC /X "/I$includeRoot" "/Fi$preprocessedPath" $HeaderPath 2>&1)
    }
    else {
        $compilerCommand = Get-Command cc -ErrorAction SilentlyContinue
        if (-not $compilerCommand) { $compilerCommand = Get-Command gcc -ErrorAction SilentlyContinue }
        if (-not $compilerCommand) { throw 'A C compiler (cc or gcc) is required for M3 preprocessing.' }
        $preprocessed = @(& $compilerCommand.Source -E -P -nostdinc -I $includeRoot -x c $HeaderPath 2>&1)
    }
    if ($LASTEXITCODE -ne 0) {
        $details = if ($IsWindows -or $env:OS -eq 'Windows_NT') { $compilerOutput } else { $preprocessed }
        throw "Frozen header preprocessing failed: $($details -join [Environment]::NewLine)"
    }
    if (-not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
        [IO.File]::WriteAllText($preprocessedPath, ($preprocessed -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    }
    if (-not (Test-Path -LiteralPath $preprocessedPath)) { throw 'The C preprocessor did not produce an output file.' }

    & dotnet run --project (Join-Path $root 'tools\BindingGenerator\BindingGenerator.csproj') -c Release -- `
        --header $HeaderPath `
        --preprocessed $preprocessedPath `
        --m2-manifest $manifestPath `
        --classification $classificationPath `
        --unsupported $unsupportedPath `
        --output $outputRoot
    if ($LASTEXITCODE -ne 0) { throw 'The structured M3 binding generator failed.' }

    $relativeOutputs = @(
        'compatibility/m3-normalized-api.json',
        'compatibility/m3-api-inventory.json',
        'compatibility/m3-coverage-summary.json',
        'src/JYPPX.ROCm.MIGraphX.CSharp.API/Generated/NativeMethods.g.cs',
        'src/JYPPX.ROCm.MIGraphX.CSharp.API/Generated/NativeMethods.LibraryImport.g.cs',
        'src/JYPPX.ROCm.MIGraphX.CSharp.API/Generated/NativeMethods.DllImport.g.cs'
    )

    if ($Verify) {
        foreach ($relativePath in $relativeOutputs) {
            $generatedPath = Join-Path $outputRoot $relativePath
            $trackedPath = Join-Path $root $relativePath
            if (-not (Test-Path -LiteralPath $trackedPath)) { throw "Generated output is missing: $relativePath" }
            $generatedBytes = [IO.File]::ReadAllBytes($generatedPath)
            $trackedBytes = [IO.File]::ReadAllBytes($trackedPath)
            if (-not [Linq.Enumerable]::SequenceEqual[byte]($generatedBytes, $trackedBytes)) {
                throw "Generated output drifted: $relativePath"
            }
        }
    }
    else {
        $suffix = '.m3tmp-' + [Guid]::NewGuid().ToString('N')
        $staged = @()
        try {
            foreach ($relativePath in $relativeOutputs) {
                $generatedPath = Join-Path $outputRoot $relativePath
                $trackedPath = Join-Path $root $relativePath
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $trackedPath) | Out-Null
                $stagedPath = $trackedPath + $suffix
                $backupPath = $trackedPath + $suffix + '.backup'
                [IO.File]::Copy($generatedPath, $stagedPath, $true)
                $staged += [PSCustomObject]@{
                    Temporary = $stagedPath
                    Target = $trackedPath
                    Backup = $backupPath
                    HadTarget = Test-Path -LiteralPath $trackedPath
                    Replaced = $false
                }
            }
            foreach ($item in $staged) {
                if ($item.HadTarget) {
                    [IO.File]::Move($item.Target, $item.Backup)
                }
                [IO.File]::Move($item.Temporary, $item.Target, $true)
                $item.Replaced = $true
            }
            foreach ($item in $staged) {
                if (Test-Path -LiteralPath $item.Backup) { Remove-Item -Force -LiteralPath $item.Backup }
            }
        }
        catch {
            foreach ($item in $staged) {
                if (Test-Path -LiteralPath $item.Backup) {
                    if (Test-Path -LiteralPath $item.Target) { Remove-Item -Force -LiteralPath $item.Target }
                    [IO.File]::Move($item.Backup, $item.Target)
                }
                elseif ($item.Replaced -and -not $item.HadTarget -and (Test-Path -LiteralPath $item.Target)) {
                    Remove-Item -Force -LiteralPath $item.Target
                }
            }
            throw
        }
        finally {
            foreach ($item in $staged) {
                if (Test-Path -LiteralPath $item.Temporary) { Remove-Item -Force -LiteralPath $item.Temporary }
                if (Test-Path -LiteralPath $item.Backup) { Remove-Item -Force -LiteralPath $item.Backup }
            }
        }
    }
}
finally {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    if ($resolvedTemporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -Recurse -Force -LiteralPath $resolvedTemporaryRoot
    }
}

Write-Output "M3 structured interop generation $($(if ($Verify) { 'verification' } else { 'update' })) passed for $actualHash."
