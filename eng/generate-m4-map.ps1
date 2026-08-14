[CmdletBinding()]
param([switch] $Verify)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$inventoryPath = Join-Path $root 'compatibility\m3-api-inventory.json'
$overridesPath = Join-Path $root 'compatibility\m4-high-level-overrides.json'
$outputPath = Join-Path $root 'compatibility\m4-high-level-api-map.json'
$inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
$overrides = Get-Content -Raw -LiteralPath $overridesPath | ConvertFrom-Json

$byId = @{}
foreach ($mapping in $overrides.mappings) {
    if ($byId.ContainsKey($mapping.id)) { throw "Duplicate M4 override id: $($mapping.id)" }
    $byId[$mapping.id] = $mapping
}

$mappings = @()
foreach ($item in $inventory.items) {
    if ($byId.ContainsKey($item.id)) {
        $override = $byId[$item.id]
        $mappings += [ordered]@{
            id = $item.id
            kind = $item.kind
            cName = $item.cName
            supportStatus = 'supported'
            publicMembers = @($override.publicMembers)
            ownership = $override.ownership
            validationLevel = 'fake-native-executed'
            tests = @($override.tests)
        }
        $byId.Remove($item.id)
    }
    elseif ($item.classification -eq 'unsupported') {
        $mappings += [ordered]@{
            id = $item.id
            kind = $item.kind
            cName = $item.cName
            supportStatus = 'unsupported'
            publicMembers = @()
            ownership = 'No safe high-level projection is defined.'
            validationLevel = 'statically-verified'
            tests = @('BindingGeneratorTests.InventoryClassificationIsClosedAndMatchesFrozenCounts')
        }
    }
    else {
        $mappings += [ordered]@{
            id = $item.id
            kind = $item.kind
            cName = $item.cName
            supportStatus = 'planned'
            publicMembers = @()
            ownership = 'No M4 public ownership contract; raw declaration remains internal.'
            validationLevel = 'planned'
            tests = @()
        }
    }
}
if ($byId.Count -ne 0) { throw "M4 overrides reference unknown ids: $($byId.Keys -join ', ')" }

$counts = [ordered]@{}
foreach ($status in @('supported', 'planned', 'unsupported')) {
    $counts[$status] = @($mappings | Where-Object supportStatus -eq $status).Count
}
$document = [ordered]@{
    '$schema' = './schemas/m4-high-level-api-map.schema.json'
    schemaVersion = '1.0.0'
    generatedAtUtc = '2026-08-14T00:00:00Z'
    sourceInventory = 'compatibility/m3-api-inventory.json'
    sourceHeaderSha256 = $inventory.sourceHeaderSha256
    evidenceBoundary = 'M4 high-level behavior is fake-native-executed only; the four M1/M2 official runtime records are unchanged.'
    counts = $counts
    mappings = $mappings
}
$json = $document | ConvertTo-Json -Depth 8

if ($Verify) {
    if (-not (Test-Path -LiteralPath $outputPath)) { throw 'M4 high-level API map is missing.' }
    $expected = $json | ConvertFrom-Json | ConvertTo-Json -Depth 8 -Compress
    $actual = Get-Content -Raw -LiteralPath $outputPath | ConvertFrom-Json | ConvertTo-Json -Depth 8 -Compress
    if ($actual -cne $expected) { throw 'M4 high-level API map is stale. Run eng/generate-m4-map.ps1.' }
    Write-Output "M4 high-level API map verified: $($mappings.Count) items ($($counts.supported) supported, $($counts.planned) planned, $($counts.unsupported) unsupported)."
}
else {
    [IO.File]::WriteAllText($outputPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output $outputPath
}
