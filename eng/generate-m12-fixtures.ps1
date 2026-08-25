[CmdletBinding()]
param(
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\models\m12'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Add-Varint {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][uint64] $Value
    )
    while ($Value -ge 0x80) {
        [void]$Buffer.Add([byte](($Value -band 0x7f) -bor 0x80))
        $Value = $Value -shr 7
    }
    [void]$Buffer.Add([byte]$Value)
}

function Add-Key {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][int] $WireType
    )
    Add-Varint -Buffer $Buffer -Value ([uint64](($Field -shl 3) -bor $WireType))
}

function Add-BytesField {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][byte[]] $Value
    )
    Add-Key -Buffer $Buffer -Field $Field -WireType 2
    Add-Varint -Buffer $Buffer -Value ([uint64]$Value.Length)
    if ($Value.Length -gt 0) { [void]$Buffer.AddRange($Value) }
}

function Add-StringField {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][string] $Value
    )
    Add-BytesField -Buffer $Buffer -Field $Field -Value ([Text.Encoding]::UTF8.GetBytes($Value))
}

function Add-VarintField {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][uint64] $Value
    )
    Add-Key -Buffer $Buffer -Field $Field -WireType 0
    Add-Varint -Buffer $Buffer -Value $Value
}

function New-Message {
    param([Parameter(Mandatory)][scriptblock] $Body)
    $buffer = [Collections.Generic.List[byte]]::new()
    & $Body $buffer
    Write-Output -NoEnumerate ([byte[]]$buffer.ToArray())
}

function New-TensorShapeDimension {
    param([Parameter(Mandatory)][uint64] $Size)
    New-Message { param($b) Add-VarintField -Buffer $b -Field 1 -Value $Size }
}

function New-TensorShape {
    param([Parameter(Mandatory)][uint64[]] $Sizes)
    New-Message {
        param($b)
        foreach ($size in $Sizes) {
            Add-BytesField -Buffer $b -Field 2 -Value (New-TensorShapeDimension -Size $size)
        }
    }
}

function New-AttrType {
    param([Parameter(Mandatory)][uint64] $DataType)
    New-Message { param($b) Add-VarintField -Buffer $b -Field 6 -Value $DataType }
}

function New-AttrShape {
    param([Parameter(Mandatory)][uint64[]] $Sizes)
    New-Message { param($b) Add-BytesField -Buffer $b -Field 7 -Value (New-TensorShape -Sizes $Sizes) }
}

function New-AttrEntry {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][byte[]] $Value
    )
    New-Message {
        param($b)
        Add-StringField -Buffer $b -Field 1 -Value $Name
        Add-BytesField -Buffer $b -Field 2 -Value $Value
    }
}

function New-Node {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Operation,
        [string[]] $Inputs,
        [hashtable[]] $Attributes
    )
    New-Message {
        param($b)
        Add-StringField -Buffer $b -Field 1 -Value $Name
        Add-StringField -Buffer $b -Field 2 -Value $Operation
        if ($null -ne $Inputs) {
            foreach ($input in $Inputs) { Add-StringField -Buffer $b -Field 3 -Value $input }
        }
        if ($null -ne $Attributes) {
            foreach ($attribute in $Attributes) {
                Add-BytesField -Buffer $b -Field 5 -Value (New-AttrEntry -Name $attribute.Name -Value $attribute.Value)
            }
        }
    }
}

function New-TensorFlowGraph {
    $float = 1
    $inputNode = New-Node -Name 'input' -Operation 'Placeholder' -Attributes @(
        @{ Name = 'dtype'; Value = (New-AttrType -DataType $float) },
        @{ Name = 'shape'; Value = (New-AttrShape -Sizes ([uint64[]](1, 4))) }
    )
    $outputNode = New-Node -Name 'output' -Operation 'Identity' -Inputs @('input') -Attributes @(
        @{ Name = 'T'; Value = (New-AttrType -DataType $float) }
    )
    New-Message {
        param($b)
        Add-BytesField -Buffer $b -Field 1 -Value $inputNode
        Add-BytesField -Buffer $b -Field 1 -Value $outputNode
        $versions = New-Message { param($v) Add-VarintField -Buffer $v -Field 1 -Value 0 }
        Add-BytesField -Buffer $b -Field 4 -Value $versions
    }
}

$tensorflowPath = Join-Path $OutputDirectory 'm12-tensorflow-minimal.pb'
[IO.File]::WriteAllBytes($tensorflowPath, (New-TensorFlowGraph))

$calibrationPath = Join-Path $OutputDirectory 'm12-calibration-map.json'
$json = @'
{
  "schemaVersion": "1.0.0",
  "format": "migraphx-calibration-map",
  "modelId": "m12-identity-float32-1x4",
  "generatedBy": "MIGraphX-CSharp-API/eng/generate-m12-fixtures.ps1",
  "license": "Apache-2.0 project-generated fixture",
  "inputs": [
    {
      "name": "input",
      "dataType": "float32",
      "scale": 0.25,
      "zeroPoint": 0
    }
  ],
  "outputs": [
    {
      "name": "output",
      "dataType": "float32",
      "scale": 0.25,
      "zeroPoint": 0
    }
  ]
}
'@
[IO.File]::WriteAllText($calibrationPath, $json.TrimStart([char]10, [char]13) + [char]10, [Text.UTF8Encoding]::new($false))

@(
    [pscustomobject]@{
        Name = 'tensorflow-minimal'
        FileName = [IO.Path]::GetFileName($tensorflowPath)
        Path = $tensorflowPath
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $tensorflowPath).Hash.ToLowerInvariant()
        Bytes = ([IO.File]::ReadAllBytes($tensorflowPath)).Length
        License = 'Apache-2.0 project-generated fixture'
        Format = 'tensorflow-graphdef'
        NodeCount = 2
    }
    [pscustomobject]@{
        Name = 'quantization-calibration'
        FileName = [IO.Path]::GetFileName($calibrationPath)
        Path = $calibrationPath
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $calibrationPath).Hash.ToLowerInvariant()
        Bytes = ([IO.File]::ReadAllBytes($calibrationPath)).Length
        License = 'Apache-2.0 project-generated fixture'
        Format = 'migraphx-calibration-map'
        NodeCount = 0
    }
)
