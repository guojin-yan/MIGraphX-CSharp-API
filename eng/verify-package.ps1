[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $PackagePath,
    [string] $Version = '0.0.0',
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string] $RepositoryCommit,
    [switch] $SkipConsumers
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
& (Join-Path $PSScriptRoot 'verify-m4-coverage.ps1') | Out-Host
$packagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$expectedFrameworks = @(
    'net46', 'net461', 'net462', 'net47', 'net471', 'net472', 'net48', 'net481',
    'netcoreapp3.1', 'net5.0', 'net6.0', 'net7.0', 'net8.0', 'net9.0', 'net10.0'
)

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($framework in $expectedFrameworks) {
        foreach ($extension in @('dll', 'xml')) {
            $expected = "lib/$framework/JYPPX.ROCm.MIGraphX.CSharp.API.$extension"
            if ($expected -notin $entries) {
                throw "Package is missing $expected."
            }
        }
    }

    $forbidden = @($entries | Where-Object {
        $_ -match '(^|/)(bin|obj|TestResults|plan|Radeon_Cloud|downloads|models)(/|$)' -or
        $_ -match '\.(cs|pdb|snupkg|onnx|so|dylib)$' -or
        $_ -match '(?i)(fake[-_]?native|migraphx_c|\.obj$|\.lib$|\.exp$)' -or
        $_ -match '(?i)(BindingGenerator|m3-(normalized|api-inventory|coverage|abi-export))' -or
        $_ -match '(?i)(unit|projectquality|packagetests)\.dll$'
    })
    if ($forbidden.Count -ne 0) {
        throw "Package contains forbidden entries: $($forbidden -join ', ')"
    }

    $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
    if ($nuspecEntries.Count -ne 1) {
        throw "Expected exactly one nuspec but found $($nuspecEntries.Count)."
    }
    $nuspecEntry = $nuspecEntries[0]
    $reader = [IO.StreamReader]::new($nuspecEntry.Open())
    try { $nuspecText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    [xml] $nuspec = $nuspecText
    $metadata = $nuspec.package.metadata
    if ($metadata.id -ne 'JYPPX.ROCm.MIGraphX.CSharp.API' -or $metadata.version -ne $Version) {
        throw 'Package identity or version is incorrect.'
    }
    foreach ($field in @('authors', 'description', 'tags', 'projectUrl', 'repository', 'readme', 'releaseNotes')) {
        if (-not $metadata.$field) { throw "NuGet metadata is missing '$field'." }
    }
    if ($metadata.repository.commit -notmatch '^[a-f0-9]{40}$') {
        throw 'NuGet repository metadata must contain a 40-character commit SHA.'
    }
    if (-not [string]::IsNullOrWhiteSpace($RepositoryCommit) -and $metadata.repository.commit -ne $RepositoryCommit) {
        throw "NuGet repository commit mismatch: expected $RepositoryCommit, actual $($metadata.repository.commit)."
    }
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    if (-not $license -or $license.type -ne 'expression' -or $license.InnerText -ne 'Apache-2.0') {
        throw 'NuGet license metadata must use the Apache-2.0 expression.'
    }
    if ($metadata.authors -ne 'Guojin Yan' -or $metadata.copyright -ne 'Copyright 2026 Guojin Yan') {
        throw 'NuGet authors or copyright metadata is incorrect.'
    }
    if (@($metadata.SelectNodes(".//*[local-name()='dependency']")).Count -ne 0) {
        throw 'The core package must not have runtime NuGet dependencies.'
    }
    foreach ($requiredEntry in @('LICENSE', 'NOTICE')) {
        if ($requiredEntry -notin $entries) {
            throw "Package is missing $requiredEntry."
        }
    }
    foreach ($entry in $archive.Entries | Where-Object { $_.Length -gt 0 }) {
        $stream = $entry.Open()
        try {
            $memory = [IO.MemoryStream]::new()
            $stream.CopyTo($memory)
            $bytes = $memory.ToArray()
            $memory.Dispose()
        }
        finally {
            $stream.Dispose()
        }
        $utf8 = [Text.Encoding]::UTF8.GetString($bytes)
        $utf16 = [Text.Encoding]::Unicode.GetString($bytes)
        $forbiddenMarkers = @(
            ('E:' + [IO.Path]::DirectorySeparatorChar + 'GitSpace'),
            ('C:' + [IO.Path]::DirectorySeparatorChar + 'Users' + [IO.Path]::DirectorySeparatorChar + 'guoji'),
            ('MIGraphX-CSharp-API' + [IO.Path]::DirectorySeparatorChar + 'plan'),
            ('Radeon_Cloud')
        )
        foreach ($needle in $forbiddenMarkers) {
            if ($utf8.Contains($needle) -or $utf16.Contains($needle)) {
                throw "Package entry '$($entry.FullName)' contains a forbidden local path or outer-workspace marker: $needle"
            }
        }
    }
}
finally {
    $archive.Dispose()
}

if (-not $SkipConsumers) {
    $consumerConfig = Join-Path $root 'tests\fixtures\package-consumers\NuGet.Config'
    $consumerPackages = Join-Path $root "artifacts\package-audit\$([Guid]::NewGuid().ToString('N'))"
    foreach ($framework in @('net46', 'netcoreapp3.1', 'net7.0', 'net10.0')) {
        $project = Join-Path $root "tests\fixtures\package-consumers\$framework\Consumer.csproj"
        & dotnet restore $project --configfile $consumerConfig --packages $consumerPackages --no-http-cache --force-evaluate -p:MIGraphXConsumerVersion=$Version
        if ($LASTEXITCODE -ne 0) { throw "Consumer restore failed for $framework." }
        & dotnet build $project -c Release --no-restore -p:MIGraphXConsumerVersion=$Version
        if ($LASTEXITCODE -ne 0) { throw "Consumer build failed for $framework." }
    }

    # Execute one managed-only consumer on the current SDK runtime. The program
    # intentionally touches no native entry point; this proves that the packed
    # assembly can load and run its value-level M12 surface after clean restore.
    $managedConsumer = Join-Path $root 'tests\fixtures\package-consumers\net10.0\bin\Release\net10.0\Consumer.dll'
    if (-not (Test-Path -LiteralPath $managedConsumer -PathType Leaf)) {
        throw "Managed consumer output is missing: $managedConsumer"
    }
    & dotnet $managedConsumer
    if ($LASTEXITCODE -ne 0) { throw 'Managed-only package consumer execution failed for net10.0.' }
    Write-Output 'Managed-only package consumer execution passed: net10.0.'
}

Write-Output "Package audit passed: $packagePath"
