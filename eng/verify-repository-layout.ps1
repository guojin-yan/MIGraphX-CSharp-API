[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
$workspace = Get-WorkspaceRoot
$violations = [Collections.Generic.List[string]]::new()
$reservedDirectories = @('artifacts', '.cache', 'api', '_site', 'downloads', 'test-results', 'TestResults', 'plan', 'Radeon_Cloud', 'compatibility', 'pack')
foreach ($name in $reservedDirectories) {
    if (Test-Path -LiteralPath (Join-Path $root $name)) { $violations.Add($name) }
}
foreach ($name in @('design', 'validation', 'articles')) {
    if (Test-Path -LiteralPath (Join-Path $root "docs/$name")) { $violations.Add("docs/$name (development record)") }
}
foreach ($contract in Get-ChildItem -LiteralPath (Join-Path $root 'eng/contracts') -Recurse -File -Filter '*.json') {
    if ((Get-Content -Raw -LiteralPath $contract.FullName) -match '"(?:stage|milestone|plannedStage|authorization|candidateVersion|validationStatus|m\d+Status)"\s*:') {
        $violations.Add([IO.Path]::GetRelativePath($root, $contract.FullName) + ' (development history in tooling contract)')
    }
}
$pending = [Collections.Generic.Stack[string]]::new()
$pending.Push($root)
while ($pending.Count -gt 0) {
    foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
        if ($item.Name -match '(?i)(?:^|[-_.])m\d+(?:[-_.]|[A-Z])') {
            $violations.Add([IO.Path]::GetRelativePath($root, $item.FullName) + ' (development stage name)')
        }
        if ($item.PSIsContainer) {
            if ($item.Name -in @('.git', '.vs', '.vscode', '.idea') -or
                ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            if ($item.Name -in @('bin', 'obj', 'TestResults', '__pycache__')) {
                $violations.Add([IO.Path]::GetRelativePath($root, $item.FullName))
            } else { $pending.Push($item.FullName) }
        } elseif ($item.Extension -in @('.obj', '.lib', '.exp', '.dll', '.so', '.dylib', '.exe', '.pdb', '.nupkg', '.snupkg', '.trx', '.onnx', '.pb', '.bundle', '.log', '.jsonl', '.coverage', '.coveragexml') -or $item.Name -match '\.so\.') {
            $violations.Add([IO.Path]::GetRelativePath($root, $item.FullName))
        }
    }
}
if ($violations.Count -gt 0) {
    throw "Source repository contains output or development records. Move them outside the checkout:`n$($violations -join "`n")"
}
Write-Output "Repository layout passed; generated output belongs in $workspace."
