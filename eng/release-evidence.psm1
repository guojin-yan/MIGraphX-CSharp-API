Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ReleaseSha256 {
    param([Parameter(Mandatory)][string] $Path)

    $stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = $sha.ComputeHash($stream) } finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
    return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}

function Get-ReleaseBytesSha256 {
    param([Parameter(Mandatory)][byte[]] $Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($Bytes) } finally { $sha.Dispose() }
    return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}

function Get-ReleasePackageIdentity {
    param([Parameter(Mandatory)][string] $Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $archive = [IO.Compression.ZipFile]::OpenRead($resolved)
    try {
        $files = @($archive.Entries |
            Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
            Sort-Object FullName |
            ForEach-Object {
                $stream = $_.Open()
                try {
                    $memory = [IO.MemoryStream]::new()
                    try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $memory.Dispose() }
                }
                finally { $stream.Dispose() }
                [pscustomobject][ordered]@{
                    path = $_.FullName.Replace('\\', '/')
                    size = $bytes.Length
                    sha256 = Get-ReleaseBytesSha256 -Bytes $bytes
                }
            })

        $canonical = ($files | ForEach-Object { "$($_.path)`0$($_.size)`0$($_.sha256)`n" }) -join ''
        $normalized = Get-ReleaseBytesSha256 -Bytes ([Text.Encoding]::UTF8.GetBytes($canonical))

        $nuspecEntry = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($nuspecEntry.Count -ne 1) { throw "Package must contain exactly one nuspec: $resolved" }
        $reader = [IO.StreamReader]::new($nuspecEntry[0].Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.package.metadata
        $licenseNode = $metadata.SelectSingleNode("*[local-name()='license']")
        $license = if ($licenseNode.type -eq 'expression') { $licenseNode.InnerText } else { 'Apache-2.0 (packaged license file)' }

        return [pscustomobject][ordered]@{
            id = [string] $metadata.id
            version = [string] $metadata.version
            path = $resolved
            size = (Get-Item -LiteralPath $resolved).Length
            sha256 = Get-ReleaseSha256 -Path $resolved
            normalizedContentSha256 = $normalized
            repositoryCommit = [string] $metadata.repository.commit
            license = $license
            files = $files
        }
    }
    finally { $archive.Dispose() }
}

function Write-ReleaseJson {
    param(
        [Parameter(Mandatory)] $Value,
        [Parameter(Mandatory)][string] $Path
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $json = $Value | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText($Path, $json + "`n", [Text.UTF8Encoding]::new($false))
}

Export-ModuleMember -Function Get-ReleaseSha256, Get-ReleasePackageIdentity, Write-ReleaseJson
