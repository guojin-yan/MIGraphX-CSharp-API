[CmdletBinding()]
param(
    [string] $HipSharpRepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$map = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m6-high-level-api-map.json') | ConvertFrom-Json
if ($map.counts.inventory -ne 192 -or $map.counts.supported -ne 75 -or $map.counts.planned -ne 116 -or $map.counts.unsupported -ne 1) {
    throw 'M6 mapping counts must be 75/116/1 over the fixed 192-item inventory.'
}
$async = @($map.mappings | Where-Object id -eq 'function:migraphx_program_run_async')
if ($async.Count -ne 1 -or $async[0].supportStatus -ne 'supported' -or @($async[0].publicMembers).Count -lt 4 -or @($async[0].tests).Count -lt 2) {
    throw 'M6 async mapping is not closed with public members and behavior tests.'
}
$contexts = @($map.mappings | Where-Object { $_.id -in @('function:migraphx_program_experimental_get_context', 'function:migraphx_context_finish', 'function:migraphx_context_get_queue') })
$nonplannedContexts = @($contexts | Where-Object { $_.supportStatus -ne 'planned' })
if ($contexts.Count -ne 3 -or $nonplannedContexts.Count -ne 0) {
    throw 'Experimental context/queue entries must remain planned in M6.'
}
$ownership = Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m6-public-ownership.json') | ConvertFrom-Json
if (@($ownership.types).Count -ne 6) { throw 'M6 ownership closure must contain six lease/copy records.' }
$adapterProject = Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop\JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop.csproj'
if (-not (Test-Path -LiteralPath $adapterProject -PathType Leaf)) { throw 'M6 adapter project is missing.' }
$projectText = Get-Content -Raw -LiteralPath $adapterProject
foreach ($required in @('JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop', 'JYPPX.ROCm.MIGraphX.CSharp.API.csproj', 'JYPPX.ROCm.HipSharp.csproj')) {
    if (-not $projectText.Contains($required, [StringComparison]::Ordinal)) { throw "M6 adapter project is missing '$required'." }
}
foreach ($forbidden in @('System.IntPtr', 'System.UIntPtr', 'SafeHandle', 'IHipNativeApi', 'IHipPointerOwner', 'DangerousGetHandle')) {
    if ((Get-Content -Raw -LiteralPath (Join-Path $root 'compatibility\m6-adapter-public-api.txt')).Contains($forbidden, [StringComparison]::Ordinal)) {
        throw "M6 adapter public baseline leaks forbidden native surface '$forbidden'."
    }
}
$generated = Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Generated\NativeMethods.DllImport.g.cs')
if (-not $generated.Contains('ProgramRunAsync(IntPtr @out, IntPtr program, IntPtr @params, IntPtr s, IntPtr name)', [StringComparison]::Ordinal)) {
    throw 'Generated async ABI does not preserve void* stream and const char* name widths.'
}
$hipRootCandidate = if ([string]::IsNullOrWhiteSpace($HipSharpRepositoryRoot)) {
    Join-Path $root (Join-Path '..' (Join-Path '..' (Join-Path 'HIP-CSharp-API' 'HIP-CSharp-API')))
}
else {
    $HipSharpRepositoryRoot
}
if (-not (Test-Path -LiteralPath $hipRootCandidate -PathType Container)) {
    throw "M6 requires a HIP-CSharp-API source root. Pass -HipSharpRepositoryRoot with the exact sibling checkout; attempted '$hipRootCandidate'."
}
$hipRoot = (Resolve-Path -LiteralPath $hipRootCandidate).Path
foreach ($friend in @(
    (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Properties\AssemblyInfo.cs'),
    (Join-Path $hipRoot 'src\JYPPX.ROCm.HipSharp\Properties\AssemblyInfo.cs'))) {
    if (-not (Get-Content -Raw -LiteralPath $friend).Contains('JYPPX.ROCm.MIGraphX.CSharp.API.HIP.Interop', [StringComparison]::Ordinal)) { throw "M6 friend assembly is missing in $friend." }
}
Write-Output 'M6 coverage gate passed: async ABI, adapter boundary, 75/116/1 mapping, six ownership records, and planned context entries are closed.'
