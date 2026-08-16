[CmdletBinding()]
param(
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\models\m11'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Add-Varint {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][uint64] $Value
    )
    while ($Value -ge 0x80) {
        $Buffer.Add([byte](($Value -band 0x7f) -bor 0x80))
        $Value = $Value -shr 7
    }
    $Buffer.Add([byte]$Value)
}

function Add-Key {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][int] $WireType
    )
    Add-Varint -Buffer $Buffer -Value ([uint64](($Field -shl 3) -bor $WireType))
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

function Add-BytesField {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][byte[]] $Value
    )
    Add-Key -Buffer $Buffer -Field $Field -WireType 2
    Add-Varint -Buffer $Buffer -Value ([uint64]$Value.Length)
    $Buffer.AddRange($Value)
}

function Add-StringField {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][string] $Value
    )
    Add-BytesField -Buffer $Buffer -Field $Field -Value ([Text.Encoding]::UTF8.GetBytes($Value))
}

function New-Message {
    param([Parameter(Mandatory)][scriptblock] $Body)
    $buffer = [Collections.Generic.List[byte]]::new()
    & $Body $buffer
    return $buffer.ToArray()
}

function New-FixedDimension {
    param([uint64] $Value)
    New-Message { param($b) Add-VarintField $b 1 $Value }
}

function New-SymbolicDimension {
    param([string] $Name)
    New-Message { param($b) Add-StringField $b 2 $Name }
}

function New-ValueInfo {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][byte[][]] $Dimensions
    )
    New-Message {
        param($b)
        $shape = New-Message {
            param($shapeBuffer)
            foreach ($dimension in $Dimensions) { Add-BytesField $shapeBuffer 1 $dimension }
        }
        $tensorType = New-Message {
            param($tensorBuffer)
            Add-VarintField $tensorBuffer 1 1
            Add-BytesField $tensorBuffer 2 $shape
        }
        $type = New-Message { param($typeBuffer) Add-BytesField $typeBuffer 1 $tensorType }
        Add-StringField $b 1 $Name
        Add-BytesField $b 2 $type
    }
}

function New-Node {
    param(
        [Parameter(Mandatory)][string] $NodeInput,
        [Parameter(Mandatory)][string] $Output,
        [Parameter(Mandatory)][string] $Operation
    )
    New-Message {
        param($b)
        Add-StringField $b 1 $NodeInput
        Add-StringField $b 2 $Output
        Add-StringField $b 4 $Operation
    }
}

function New-Model {
    param(
        [Parameter(Mandatory)][string] $GraphName,
        [Parameter(Mandatory)][byte[][]] $Nodes,
        [Parameter(Mandatory)][byte[]] $ModelInput,
        [Parameter(Mandatory)][byte[][]] $Outputs
    )
    $graph = New-Message {
        param($b)
        foreach ($node in $Nodes) { Add-BytesField $b 1 $node }
        Add-StringField $b 2 $GraphName
        Add-BytesField $b 11 $ModelInput
        foreach ($output in $Outputs) { Add-BytesField $b 12 $output }
    }
    $opset = New-Message { param($b) Add-VarintField $b 2 13 }
    New-Message {
        param($b)
        Add-VarintField $b 1 8
        Add-StringField $b 2 'MIGraphX-CSharp-API-M11'
        Add-BytesField $b 7 $graph
        Add-BytesField $b 8 $opset
    }
}

$fixedDimensions = [byte[][]]@((New-FixedDimension 2), (New-FixedDimension 2))
$multiOutput = New-Model `
    -GraphName 'm11_multi_output_float32' `
    -Nodes ([byte[][]]@((New-Node 'input' 'identity_output' 'Identity'), (New-Node 'input' 'neg_output' 'Neg'))) `
    -ModelInput (New-ValueInfo 'input' $fixedDimensions) `
    -Outputs ([byte[][]]@((New-ValueInfo 'identity_output' $fixedDimensions), (New-ValueInfo 'neg_output' $fixedDimensions)))

$dynamicDimensions = [byte[][]]@((New-FixedDimension 1), (New-FixedDimension 4))
$dynamicIdentity = New-Model `
    -GraphName 'm11_dynamic_identity_float32' `
    -Nodes ([byte[][]]@((New-Node 'input' 'output' 'Identity'))) `
    -ModelInput (New-ValueInfo 'input' $dynamicDimensions) `
    -Outputs ([byte[][]]@((New-ValueInfo 'output' $dynamicDimensions)))

$identityPath = Join-Path $OutputDirectory 'm2-identity-float32.onnx'
& (Join-Path $PSScriptRoot 'generate-m2-model.ps1') -OutputPath $identityPath | Out-Null
$fixtures = @(
    [ordered]@{ Name = 'identity'; FileName = 'm2-identity-float32.onnx'; Graph = 'Identity(float32[1,4])'; Bytes = [IO.File]::ReadAllBytes($identityPath) },
    [ordered]@{ Name = 'multi-output'; FileName = 'm11-multi-output-float32.onnx'; Graph = 'Identity+Neg(float32[2,2], ordered outputs)'; Bytes = $multiOutput },
    [ordered]@{ Name = 'dynamic-identity'; FileName = 'm11-dynamic-identity-float32.onnx'; Graph = 'Identity(float32[1,4], runtime shape overrides)'; Bytes = $dynamicIdentity }
)

$result = foreach ($fixture in $fixtures) {
    $path = Join-Path $OutputDirectory $fixture.FileName
    [IO.File]::WriteAllBytes($path, $fixture.Bytes)
    [pscustomobject]@{
        Name = $fixture.Name
        FileName = $fixture.FileName
        Path = $path
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        Bytes = $fixture.Bytes.Length
        Graph = $fixture.Graph
        License = 'Apache-2.0 project-generated fixture'
    }
}
$result
