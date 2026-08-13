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
