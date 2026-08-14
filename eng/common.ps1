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
