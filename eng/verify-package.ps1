[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $PackagePath,
    [string] $Version = '0.0.0',
    [switch] $SkipConsumers
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
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
        $_ -match '\.(pdb|onnx|so|dylib)$' -or
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
    foreach ($field in @('authors', 'description', 'tags', 'projectUrl', 'repository', 'readme')) {
        if (-not $metadata.$field) { throw "NuGet metadata is missing '$field'." }
    }
    if ($metadata.repository.commit -notmatch '^[a-f0-9]{40}$') {
        throw 'NuGet repository metadata must contain a 40-character commit SHA.'
    }
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    if (-not $license -or $license.type -ne 'expression' -or $license.InnerText -ne 'Apache-2.0') {
        throw 'NuGet license metadata must use the Apache-2.0 expression.'
    }
    if ($metadata.authors -ne 'Guojin Yan' -or $metadata.copyright -ne 'Copyright 2026 Guojin Yan') {
        throw 'NuGet authors or copyright metadata is incorrect.'
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
        & dotnet restore $project --configfile $consumerConfig --packages $consumerPackages --no-http-cache --force-evaluate
        if ($LASTEXITCODE -ne 0) { throw "Consumer restore failed for $framework." }
        & dotnet build $project -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Consumer build failed for $framework." }
    }
}

Write-Output "Package audit passed: $packagePath"
