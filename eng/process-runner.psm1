Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CapturedProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Diagnostics.ProcessStartInfo] $StartInfo,
        [Parameter(Mandatory)][string] $OutputDirectory,
        [ValidateRange(1, 86400)][int] $TimeoutSeconds = 900
    )

    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $StartInfo.UseShellExecute = $false
    $StartInfo.RedirectStandardOutput = $true
    $StartInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($StartInfo)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = $false
        # Short waits allow PowerShell to handle cancellation while native work runs.
        while (-not $process.WaitForExit(250)) {
            if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $timedOut = $true
                try { $process.Kill($true) }
                catch { if (-not $process.HasExited) { throw } }
                $process.WaitForExit()
                break
            }
        }
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stdout.log'), $stdout.GetAwaiter().GetResult())
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'stderr.log'), $stderr.GetAwaiter().GetResult())
        $result = [ordered]@{
            exitCode = $process.ExitCode
            timedOut = $timedOut
            timeoutSeconds = $TimeoutSeconds
            durationMilliseconds = $timer.ElapsedMilliseconds
        }
        [IO.File]::WriteAllText((Join-Path $OutputDirectory 'process.json'), ($result | ConvertTo-Json))
        return [pscustomobject]$result
    }
    finally {
        try { if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() } }
        finally { $process.Dispose() }
    }
}

Export-ModuleMember -Function Invoke-CapturedProcess
