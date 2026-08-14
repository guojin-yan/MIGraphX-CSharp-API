Set-StrictMode -Version Latest

function Get-MIGraphXTarExecutable {
    [CmdletBinding()]
    param([string] $TarPath = 'tar')

    if ([IO.Path]::IsPathRooted($TarPath)) {
        if (-not (Test-Path -LiteralPath $TarPath -PathType Leaf)) {
            throw "tar executable was not found: $TarPath"
        }
        return (Resolve-Path -LiteralPath $TarPath).Path
    }

    $command = Get-Command $TarPath -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'tar is required to audit Debian runtime archives.'
    }
    return $command.Source
}

function ConvertTo-MIGraphXArchivePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [switch] $AllowRoot
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path.IndexOf([char]0) -ge 0 -or
        $Path -match '[\x00-\x1f]' -or
        $Path.Contains('\') -or
        $Path.StartsWith('/', [StringComparison]::Ordinal) -or
        $Path -match '^[A-Za-z]:') {
        throw "Archive path must be relative and portable: $Path"
    }

    $normalized = $Path
    if ($normalized.StartsWith('./', [StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }
    $normalized = $normalized.TrimEnd('/')
    if ([string]::IsNullOrEmpty($normalized)) {
        if ($AllowRoot) { return '' }
        throw "Archive path cannot name the archive root: $Path"
    }

    $segments = $normalized.Split('/')
    if ($segments -contains '' -or $segments -contains '.' -or $segments -contains '..') {
        throw "Archive path must be traversal-free: $Path"
    }
    return $normalized
}

function Assert-MIGraphXArchiveEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][char] $Type,
        [AllowNull()][string] $LinkTarget
    )

    $normalized = ConvertTo-MIGraphXArchivePath $Path -AllowRoot
    if ($Type -notin @('-', 'd', 'l')) {
        throw "Archive entry type '$Type' is not allowed for ${Path}; devices, sockets, FIFOs, and hard links are rejected."
    }
    if ($Type -ne 'l') {
        if (-not [string]::IsNullOrEmpty($LinkTarget)) {
            throw "Only a symbolic-link archive entry may declare a link target: $Path"
        }
        return
    }

    if ([string]::IsNullOrWhiteSpace($LinkTarget) -or
        $LinkTarget.IndexOf([char]0) -ge 0 -or
        $LinkTarget -match '[\x00-\x1f]' -or
        $LinkTarget.Contains('\') -or
        $LinkTarget.StartsWith('/', [StringComparison]::Ordinal) -or
        $LinkTarget -match '^[A-Za-z]:') {
        throw "Archive symbolic link target must be relative and portable: $Path -> $LinkTarget"
    }

    $resolved = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrEmpty($normalized)) {
        $parent = $normalized.Split('/')
        for ($index = 0; $index -lt $parent.Length - 1; $index++) {
            $resolved.Add($parent[$index])
        }
    }
    foreach ($segment in $LinkTarget.Split('/')) {
        if ([string]::IsNullOrEmpty($segment)) {
            throw "Archive symbolic link target contains an empty segment: $Path -> $LinkTarget"
        }
        if ($segment -eq '.') { continue }
        if ($segment -eq '..') {
            if ($resolved.Count -eq 0) {
                throw "Archive symbolic link escapes the archive root: $Path -> $LinkTarget"
            }
            $resolved.RemoveAt($resolved.Count - 1)
            continue
        }
        $resolved.Add($segment)
    }
}

function Get-MIGraphXTarListing {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $TarExecutable,
        [Parameter(Mandatory = $true)][string] $ArchivePath,
        [switch] $VerboseListing
    )

    $arguments = if ($VerboseListing) { @('-tvf', $ArchivePath) } else { @('-tf', $ArchivePath) }
    $output = @(& $TarExecutable @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "tar could not inspect archive '$ArchivePath':`n$($output -join "`n")"
    }
    return @($output | ForEach-Object { [string]$_ })
}

function Export-MIGraphXDebMember {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $TarExecutable,
        [Parameter(Mandatory = $true)][string] $DebPath,
        [Parameter(Mandatory = $true)][string] $Member,
        [Parameter(Mandatory = $true)][string] $Destination
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $TarExecutable
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-xOf', $DebPath, $Member)) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Could not start tar for Debian member: $Member" }
    $errorRead = $process.StandardError.ReadToEndAsync()
    try {
        $output = [IO.File]::Create($Destination)
        try { $process.StandardOutput.BaseStream.CopyTo($output) }
        finally { $output.Dispose() }
        $process.WaitForExit()
        $errorText = $errorRead.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            [IO.File]::Delete($Destination)
            throw "tar could not read Debian member '$Member': $errorText"
        }
    } finally {
        $process.Dispose()
    }
}

function Assert-MIGraphXTarArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $TarExecutable,
        [Parameter(Mandatory = $true)][string] $ArchivePath
    )

    $names = @(Get-MIGraphXTarListing $TarExecutable $ArchivePath)
    $verbose = @(Get-MIGraphXTarListing $TarExecutable $ArchivePath -VerboseListing)
    if ($names.Count -eq 0 -or $names.Count -ne $verbose.Count) {
        throw "Archive listing is empty or ambiguous: $ArchivePath"
    }

    for ($index = 0; $index -lt $names.Count; $index++) {
        $line = $verbose[$index]
        if ([string]::IsNullOrEmpty($line)) { throw "Archive entry type is missing: $($names[$index])" }
        $type = [char]$line[0]
        $linkTarget = $null
        if ($type -eq 'l') {
            $separator = $line.LastIndexOf(' -> ', [StringComparison]::Ordinal)
            if ($separator -lt 0) { throw "Archive symbolic link target is missing: $($names[$index])" }
            $linkTarget = $line.Substring($separator + 4)
        }
        Assert-MIGraphXArchiveEntry -Path $names[$index] -Type $type -LinkTarget $linkTarget
    }
}

function Assert-MIGraphXDebArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $DebPath,
        [Parameter(Mandatory = $true)][string] $InspectionDirectory,
        [string] $TarPath = 'tar'
    )

    $tar = Get-MIGraphXTarExecutable $TarPath
    $members = @(Get-MIGraphXTarListing $tar $DebPath)
    $required = @('debian-binary')
    $control = @($members | Where-Object { $_ -match '^control\.tar\.(?:xz|zst|gz|bz2|lzma)$' })
    $data = @($members | Where-Object { $_ -match '^data\.tar\.(?:xz|zst|gz|bz2|lzma)$' })
    if ($control.Count -ne 1 -or $data.Count -ne 1 -or
        @($members | Where-Object { $_ -notin ($required + $control + $data) }).Count -ne 0 -or
        @($members | Where-Object { $_ -eq 'debian-binary' }).Count -ne 1) {
        throw "Debian archive must contain exactly debian-binary, one control archive, and one data archive: $DebPath"
    }

    New-Item -ItemType Directory -Force -Path $InspectionDirectory | Out-Null
    foreach ($member in @($control[0], $data[0])) {
        $destination = Join-Path $InspectionDirectory $member
        if (Test-Path -LiteralPath $destination -PathType Leaf) { [IO.File]::Delete($destination) }
        Export-MIGraphXDebMember $tar $DebPath $member $destination
        Assert-MIGraphXTarArchive $tar $destination
    }
}

Export-ModuleMember -Function Assert-MIGraphXArchiveEntry, Assert-MIGraphXDebArchive
