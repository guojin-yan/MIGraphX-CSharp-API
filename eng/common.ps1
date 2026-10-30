Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepositoryRoot {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    if (-not (Test-Path -LiteralPath (Join-Path $root 'MIGraphXSharp.sln'))) {
        throw "Expected the MIGraphXSharp source repository at '$root'."
    }

    $outerPlan = Join-Path $root 'plan'
    if (Test-Path -LiteralPath $outerPlan) {
        throw "Repository boundary check failed: the outer plan directory is inside the source repository."
    }

    return $root
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)

    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-WorkspaceRoot {
    $repository = Get-RepositoryRoot
    $workspace = if ([string]::IsNullOrWhiteSpace($env:MIGRAPHX_WORKSPACE_ROOT)) {
        Join-Path (Split-Path $repository -Parent) 'MIGraphXSharp.Workspace'
    } else {
        if (-not [IO.Path]::IsPathFullyQualified($env:MIGRAPHX_WORKSPACE_ROOT)) {
            throw 'MIGRAPHX_WORKSPACE_ROOT must be an absolute path outside the source repository.'
        }
        $env:MIGRAPHX_WORKSPACE_ROOT
    }
    $workspace = [IO.Path]::GetFullPath($workspace).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if ($workspace.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or
        $workspace.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'MIGRAPHX_WORKSPACE_ROOT must be outside the source repository.'
    }
    return $workspace
}

function Get-ArtifactsRoot { return Join-Path (Get-WorkspaceRoot) 'artifacts' }
function Get-CacheRoot { return Join-Path (Get-WorkspaceRoot) '.cache' }

function Get-VisualCppEnvironment {
    param([Parameter(Mandatory)][string] $VcvarsPath)

    # vcvars uses cmd.exe, whose expanded command line is limited to 8191
    # characters. Initialize in a child with a short PATH; leave the caller's
    # environment untouched until the complete compiler environment is ready.
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $env:ComSpec
    $start.Arguments = '/d /s /c ""{0}" >nul && set"' -f $VcvarsPath
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot;$env:SystemRoot\System32\Wbem"
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorText = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Failed to initialize the Visual Studio C++ environment: $($errorText.GetAwaiter().GetResult())"
        }
        return $output.GetAwaiter().GetResult() -split '\r?\n'
    } finally { $process.Dispose() }
}

function Expand-DebDataFile {
    param(
        [Parameter(Mandatory)][string] $DebPath,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][string] $ArchiveMember
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Push-Location $Destination
    try {
        if ($IsWindows -or $env:OS -eq 'Windows_NT') {
            & tar -xf $DebPath data.tar.xz
        }
        else {
            $ar = (Get-Command ar -ErrorAction Stop).Source
            & $ar x $DebPath data.tar.xz
        }
        if ($LASTEXITCODE -ne 0) { throw 'Failed to extract data.tar.xz from the Debian package.' }
        & tar -xf data.tar.xz $ArchiveMember
        if ($LASTEXITCODE -ne 0) { throw "Failed to extract '$ArchiveMember' from data.tar.xz." }
    }
    finally {
        Pop-Location
    }
}
