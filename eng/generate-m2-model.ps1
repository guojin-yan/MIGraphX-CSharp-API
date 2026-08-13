[CmdletBinding()]
param(
    [string] $OutputPath
)

. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-RepositoryRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $root 'artifacts\models\m2-identity-float32.onnx'
}

function Add-Varint {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[byte]] $Buffer,
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
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][int] $WireType
    )

    Add-Varint -Buffer $Buffer -Value ([uint64](($Field -shl 3) -bor $WireType))
}

function Add-VarintField {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][uint64] $Value
    )

    Add-Key -Buffer $Buffer -Field $Field -WireType 0
    Add-Varint -Buffer $Buffer -Value $Value
}

function Add-BytesField {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][byte[]] $Value
    )

    Add-Key -Buffer $Buffer -Field $Field -WireType 2
    Add-Varint -Buffer $Buffer -Value ([uint64]$Value.Length)
    $Buffer.AddRange($Value)
}

function Add-StringField {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[byte]] $Buffer,
        [Parameter(Mandatory)][int] $Field,
        [Parameter(Mandatory)][string] $Value
    )

    Add-BytesField -Buffer $Buffer -Field $Field -Value ([Text.Encoding]::UTF8.GetBytes($Value))
}

function New-Message {
    param([Parameter(Mandatory)][scriptblock] $Body)

    $buffer = [System.Collections.Generic.List[byte]]::new()
    & $Body $buffer
    return $buffer.ToArray()
}

function New-Dimension {
    param([uint64] $Value)
    New-Message { param($b) Add-VarintField $b 1 $Value }
}

function New-ValueInfo {
    param([string] $Name)
    New-Message {
        param($b)
        $shape = New-Message {
            param($shapeBuffer)
            Add-BytesField $shapeBuffer 1 (New-Dimension 1)
            Add-BytesField $shapeBuffer 1 (New-Dimension 4)
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

$node = New-Message {
    param($b)
    Add-StringField $b 1 'input'
    Add-StringField $b 2 'output'
    Add-StringField $b 4 'Identity'
}
$graph = New-Message {
    param($b)
    Add-BytesField $b 1 $node
    Add-StringField $b 2 'm2_identity_float32'
    Add-BytesField $b 11 (New-ValueInfo 'input')
    Add-BytesField $b 12 (New-ValueInfo 'output')
}
$opset = New-Message { param($b) Add-VarintField $b 2 13 }
$model = New-Message {
    param($b)
    Add-VarintField $b 1 8
    Add-StringField $b 2 'MIGraphX-CSharp-API'
    Add-BytesField $b 7 $graph
    Add-BytesField $b 8 $opset
}

$parent = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $parent | Out-Null
[IO.File]::WriteAllBytes($OutputPath, $model)
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $OutputPath).Hash.ToLowerInvariant()

[PSCustomObject]@{
    ModelPath = (Resolve-Path -LiteralPath $OutputPath).Path
    Sha256 = $hash
    Bytes = $model.Length
    Graph = 'Identity(float32[1,4] -> float32[1,4]); ONNX IR 8; opset 13'
}
