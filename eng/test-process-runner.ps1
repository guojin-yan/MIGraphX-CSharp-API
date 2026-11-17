[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
Import-Module (Join-Path $PSScriptRoot 'process-runner.psm1') -Force
$output = Join-Path (Get-ArtifactsRoot) ('process-runner-tests/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$shell = (Get-Process -Id $PID).Path
function New-Start([string] $Command) {
    $start = [Diagnostics.ProcessStartInfo]::new($shell)
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-Command', $Command)) { $start.ArgumentList.Add($argument) }
    return $start
}
$normal = Invoke-CapturedProcess -StartInfo (New-Start '[Console]::Out.WriteLine("out-marker"); [Console]::Error.WriteLine("err-marker"); exit 7') -OutputDirectory (Join-Path $output 'exit') -TimeoutSeconds 30
if ($normal.exitCode -ne 7 -or $normal.timedOut -or
    (Get-Content (Join-Path $output 'exit/stdout.log') -Raw).Trim() -ne 'out-marker' -or
    (Get-Content (Join-Path $output 'exit/stderr.log') -Raw).Trim() -ne 'err-marker') {
    throw 'Captured exit status or standard output/error was lost.'
}
$pidFile = Join-Path $output 'timeout/child.pid'
$pidLiteral = $pidFile.Replace("'", "''")
$descendantPidFile = Join-Path $output 'timeout/descendant.pid'
$descendantLiteral = $descendantPidFile.Replace("'", "''")
$descendantCommand = "[IO.File]::WriteAllText('$descendantLiteral', [string]`$PID); Start-Sleep -Seconds 60"
$encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($descendantCommand))
$command = @"
[IO.File]::WriteAllText('$pidLiteral', [string]`$PID)
`$nested = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id `$PID).Path)
`$nested.UseShellExecute = `$false
`$nested.CreateNoWindow = `$true
foreach (`$argument in @('-NoProfile', '-NonInteractive', '-EncodedCommand', '$encodedCommand')) { `$nested.ArgumentList.Add(`$argument) }
`$child = [Diagnostics.Process]::Start(`$nested)
`$child.Dispose()
[Console]::Out.WriteLine('before-timeout')
Start-Sleep -Seconds 60
"@
$timeout = Invoke-CapturedProcess -StartInfo (New-Start $command) -OutputDirectory (Join-Path $output 'timeout') -TimeoutSeconds 6
if (-not $timeout.timedOut -or $timeout.exitCode -eq 0 -or
    (Get-Content (Join-Path $output 'timeout/stdout.log') -Raw).Trim() -ne 'before-timeout') {
    throw 'A timed-out child must fail and retain output written before termination.'
}
$childProcessId = [int](Get-Content -LiteralPath $pidFile -Raw)
if (Get-Process -Id $childProcessId -ErrorAction SilentlyContinue) { throw 'Timed-out child is still running.' }
$descendantProcessId = [int](Get-Content -LiteralPath $descendantPidFile -Raw)
if (Get-Process -Id $descendantProcessId -ErrorAction SilentlyContinue) { throw 'Timed-out descendant is still running.' }
Write-Output "Process runner passed: captured failure, stdout/stderr and child/descendant timeout cleanup. Evidence: $output"
