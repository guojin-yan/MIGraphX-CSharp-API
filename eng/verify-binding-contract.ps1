[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$model = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\contracts\normalized-api.json') | ConvertFrom-Json
$unsupported = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\contracts\unsupported-api.json') | ConvertFrom-Json
$overrides = Get-Content -Raw -LiteralPath (Join-Path $root 'eng\contracts\binding-overrides.json') | ConvertFrom-Json

if ($model.source.headerSha256 -ne $overrides.source.headerSha256) { throw 'The normalized model and binding manifest use different frozen headers.' }
if ($model.functions.Count -ne 159 -or $model.enums.Count -ne 2 -or $model.handles.Count -ne 25 -or $model.callbacks.Count -ne 6) {
    throw 'Normalized model counts do not match the frozen header inventory.'
}
$items = @($model.functions) + @($model.enums) + @($model.handles) + @($model.callbacks)
if ($items.Count -ne 192 -or @($items.id | Sort-Object -Unique).Count -ne 192) {
    throw 'Binding inventory IDs are not unique and closed.'
}
$allowed = @('generated', 'handwritten', 'unsupported', 'configuration-unavailable')
if (@($items | Where-Object classification -notin $allowed).Count -ne 0) { throw 'Binding inventory contains an unknown classification.' }

function Get-ClassificationCounts {
    param([object[]] $Items)
    [PSCustomObject]@{
        total = $Items.Count
        generated = @($Items | Where-Object classification -eq 'generated').Count
        handwritten = @($Items | Where-Object classification -eq 'handwritten').Count
        unsupported = @($Items | Where-Object classification -eq 'unsupported').Count
        configurationUnavailable = @($Items | Where-Object classification -eq 'configuration-unavailable').Count
    }
}
$counts = @{}
foreach ($kind in @('functions', 'enums', 'handles', 'callbacks')) {
    $counts[$kind] = Get-ClassificationCounts -Items @($model.$kind)
}
$counts.overall = Get-ClassificationCounts -Items $items
foreach ($kind in @('functions', 'enums', 'handles', 'callbacks', 'overall')) {
    $item = $counts.$kind
    if ($item.total -ne ($item.generated + $item.handwritten + $item.unsupported + $item.configurationUnavailable)) {
        throw "Binding $kind classification is not mutually exclusive and closed."
    }
}
if ($counts.functions.total -ne 159 -or $counts.functions.generated -ne 117 -or $counts.functions.handwritten -ne 41 -or $counts.functions.unsupported -ne 1) {
    throw 'Binding function classification counts drifted.'
}
if ($counts.overall.total -ne 192 -or $counts.overall.generated -ne 144 -or $counts.overall.handwritten -ne 47 -or $counts.overall.unsupported -ne 1) {
    throw 'Binding overall classification counts drifted.'
}

$unsupportedNames = @($unsupported.items | Where-Object kind -eq 'function' | ForEach-Object cName)
if ($unsupportedNames.Count -ne 1 -or $unsupportedNames[0] -ne 'migraphx_operation_create') {
    throw 'The single variadic unsupported function classification drifted.'
}
$expectedManaged = @($model.functions | Where-Object classification -ne 'unsupported' | ForEach-Object cName | Sort-Object -Unique)
$expectedHeader = @($model.functions.cName | Sort-Object -Unique)
if ($expectedManaged.Count -ne 158 -or $expectedHeader.Count -ne 159) { throw 'Managed/header function totals drifted.' }

$libraryImport = Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Generated\NativeMethods.LibraryImport.g.cs')
$dllImport = Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Generated\NativeMethods.DllImport.g.cs')
$libraryNames = @([regex]::Matches($libraryImport, 'EntryPoint\s*=\s*"(?<name>migraphx_[a-z0-9_]+)"') | ForEach-Object { $_.Groups['name'].Value } | Sort-Object -Unique)
$dllNames = @([regex]::Matches($dllImport, 'EntryPoint\s*=\s*"(?<name>migraphx_[a-z0-9_]+)"') | ForEach-Object { $_.Groups['name'].Value } | Sort-Object -Unique)
foreach ($projection in @(
    @{ Name = 'LibraryImport'; Values = $libraryNames },
    @{ Name = 'DllImport'; Values = $dllNames }
)) {
    $difference = Compare-Object $expectedManaged $projection.Values
    if ($difference) { throw "$($projection.Name) differs from the managed declaration set: $($difference | Out-String)" }
}
if ($unsupportedNames[0] -in $libraryNames -or $unsupportedNames[0] -in $dllNames) {
    throw 'The variadic unsupported function must not receive a guessed managed EntryPoint.'
}

$overrideNames = @($overrides.functions.cName | Sort-Object -Unique)
if ((Compare-Object $overrideNames @($expectedManaged | Where-Object { $_ -in $overrideNames }))) { throw 'The reviewed binding subset contains missing declarations.' }
$common = Get-Content -Raw -LiteralPath (Join-Path $root 'src\JYPPX.ROCm.MIGraphX.CSharp.API\Generated\NativeMethods.g.cs')
if (@([regex]::Matches($common, 'internal delegate NativeMIGraphXStatus NativeExperimentalCustomOp')).Count -ne 6) {
    throw 'The six callback delegates were not generated with the normalized model.'
}
if ($common.Contains((Resolve-Path $root).Path, [StringComparison]::OrdinalIgnoreCase)) { throw 'Generated output contains an absolute repository path.' }

[PSCustomObject]@{
    HeaderItems = $items.Count
    HeaderFunctions = $expectedHeader.Count
    ManagedEntryPoints = $expectedManaged.Count
    HandwrittenFunctionOverrides = $counts.functions.handwritten
    UnsupportedFunctions = $counts.functions.unsupported
    Enums = $model.enums.Count
    Handles = $model.handles.Count
    Callbacks = $model.callbacks.Count
    ClassificationClosed = $true
    Evidence = 'normalized-model/generated-source closure; statically-verified'
}
