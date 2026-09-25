<#
.SYNOPSIS
    Read-only survey of a folder of .dds texture files.

.DESCRIPTION
    Parses ONLY the DDS header of every .dds file found recursively under -Root
    (128 bytes, +20 bytes when a DX10 extension header is present) and reports:

      * FourCC histogram (DXT1/DXT3/DXT5/ATI2/BC4U/BC5U/DX10/<none>/other)
      * DXGI format histogram for DX10 files, with enum names
      * Resolution (WxH) histogram
      * MipMapCount histogram, and how it compares to the full mip chain
      * Cube map / volume texture / texture array detection
      * Uncompressed RGBA-mask format detection
      * Anomalies (short files, bad magic, unparseable headers)
      * Whether fileSize == headerBytes + sum(mipLevelSize), per file
      * Representative annotated hex header dumps
      * Which formats are eligible for endpoint hue-shifting in the recolor tool

    This script never writes to the surveyed folder. It is intended to be kept in
    the repository and re-run whenever a new texture corpus needs analysing. Its
    output determines which BCn formats the recolor tool must support.
.PARAMETER Root
    Folder to scan. Scanned recursively. Defaults to the current directory.

.PARAMETER Csv
    Optional path. When supplied, the per-file table is exported as CSV.

.PARAMETER DumpHeaders
    How many representative files to dump as annotated hex. Default 5. Use 0 to skip.

.PARAMETER SamplePerFormat
    When picking representatives, prefer this many files per distinct format. Default 1.

.PARAMETER Quiet
    Suppress the periodic progress counter.

.EXAMPLE
    pwsh -File .\tools\Invoke-DdsSurvey.ps1 -Root .\textures

.EXAMPLE
    pwsh -File .\tools\Invoke-DdsSurvey.ps1 -Root 'D:\some\corpus' -Csv .\survey.csv -DumpHeaders 8
#>
[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Csv,
    [int]$DumpHeaders = 5,
    [int]$SamplePerFormat = 1,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# DXGI_FORMAT enum subset relevant to DDS payloads.
#
# Source of truth: dxgiformat.h from the Windows SDK. To verify against the
# local copy:
#   $h = Get-ChildItem "$env:ProgramFiles(x86)\Windows Kits\10\Include" -Recurse -Filter dxgiformat.h |
#        Sort-Object FullName -Descending | Select-Object -First 1
#   Select-String -Path $h.FullName -Pattern 'BC[1-7]|R8G8B8A8|B8G8R8A8'
# ---------------------------------------------------------------------------
$DXGI_NAMES = @{
    0  = 'UNKNOWN'
    1  = 'R32G32B32A32_TYPELESS'
    2  = 'R32G32B32A32_FLOAT'
    3  = 'R32G32B32A32_UINT'
    4  = 'R32G32B32A32_SINT'
    6  = 'R32G32B32_FLOAT'
    10 = 'R16G16B16A16_FLOAT'
    11 = 'R16G16B16A16_UNORM'
    13 = 'R16G16B16A16_UINT'
    28 = 'R8G8B8A8_UNORM'
    29 = 'R8G8B8A8_UNORM_SRGB'
    31 = 'R8G8B8A8_UINT'
    41 = 'R8G8_UNORM'
    49 = 'R8_UNORM'
    70 = 'BC1_UNORM'
    71 = 'BC1_UNORM_SRGB'
    72 = 'BC2_UNORM'
    73 = 'BC2_UNORM_SRGB'
    74 = 'BC3_UNORM'
    75 = 'BC3_UNORM_SRGB'
    76 = 'BC4_UNORM'
    77 = 'BC4_SNORM'
    78 = 'BC5_UNORM'
    79 = 'BC5_SNORM'
    80 = 'BC6H_UF16'
    81 = 'BC6H_SF16'
    82 = 'BC7_UNORM'
    83 = 'BC7_UNORM_SRGB'
    87 = 'B8G8R8A8_UNORM'
    88 = 'B8G8R8A8_UNORM_SRGB'
    91 = 'B8G8R8X8_UNORM'
}

# Bytes per 4x4 block for legacy block-compressed FourCCs.
$FOURCC_BLOCK_BYTES = @{
    'DXT1' = 8
    'DXT2' = 16
    'DXT3' = 16
    'DXT4' = 16
    'DXT5' = 16
    'ATI1' = 8
    'BC4U' = 8
    'BC4S' = 8
    'ATI2' = 16
    'BC5U' = 16
    'BC5S' = 16
}

$HEADER_BYTES_BASE = 128
$HEADER_BYTES_DX10 = 148

function Get-MipLevelSizeBytes {
    <#
        Bytes for one mip level.

        Block formats always occupy a minimum of one 4x4 block, so a 1x1 or 3x3
        level still costs one full block.
    #>
    param(
        [int]$Width,
        [int]$Height,
        [int]$BlockBytes,
        [int]$BytesPerPixel,
        [int]$MipIndex
    )
    $w = [Math]::Max(1, $Width  -shr $MipIndex)
    $h = [Math]::Max(1, $Height -shr $MipIndex)
    if ($BlockBytes -gt 0) {
        $bw = [Math]::Max(1, [int][Math]::Ceiling($w / 4.0))
        $bh = [Math]::Max(1, [int][Math]::Ceiling($h / 4.0))
        return $bw * $bh * $BlockBytes
    }
    return $w * $h * $BytesPerPixel
}

function Get-BlockBytesFromDxgi {
    # DX10 files carry the real format in the extension header, so resolve block
    # size from dxgiFormat rather than the (always DX10) FourCC.
    param([int]$DxgiFormat)
    switch ($DxgiFormat) {
        70 { 8 }    # BC1_UNORM
        71 { 8 }    # BC1_UNORM_SRGB
        72 { 16 }   # BC2_UNORM
        73 { 16 }   # BC2_UNORM_SRGB
        74 { 16 }   # BC3_UNORM
        75 { 16 }   # BC3_UNORM_SRGB
        76 { 8 }    # BC4_UNORM
        77 { 8 }    # BC4_SNORM
        78 { 16 }   # BC5_UNORM
        79 { 16 }   # BC5_SNORM
        80 { 16 }   # BC6H_UF16
        81 { 16 }   # BC6H_SF16
        82 { 16 }   # BC7_UNORM
        83 { 16 }   # BC7_UNORM_SRGB
        default { 0 }
    }
}

function Get-BytesPerPixelFromDxgi {
    param([int]$DxgiFormat)
    switch ($DxgiFormat) {
        28 { 4 }    # R8G8B8A8_UNORM
        29 { 4 }    # R8G8B8A8_UNORM_SRGB
        31 { 4 }    # R8G8B8A8_UINT
        41 { 2 }    # R8G8_UNORM
        49 { 1 }    # R8_UNORM
        10 { 8 }    # R16G16B16A16_FLOAT
        11 { 8 }    # R16G16B16A16_UNORM
        13 { 8 }    # R16G16B16A16_UINT
        87 { 4 }    # B8G8R8A8_UNORM
        88 { 4 }    # B8G8R8A8_UNORM_SRGB
        91 { 4 }    # B8G8R8X8_UNORM
        default { 0 }
    }
}

function Get-FourCcLabel {
    param([string]$FourCc)
    if ([string]::IsNullOrEmpty($FourCc)) { return '<none>' }
    if ($FourCc -match '^[\x00-\x1F]+$') { return '<none>' }
    return $FourCc.Trim()
}

function Get-HueShiftEligibility {
    <#
        Can the block-level endpoint engine recolor this format?

        Only formats whose colour lives in two RGB565 endpoints per block are
        eligible: BC1/DXT1, BC2/DXT3, BC3/DXT5.
    #>
    param($Row)
    if ($null -ne $Row.DxgiFormat) {
        switch ([int]$Row.DxgiFormat) {
            70 { return 'YES      BC1 endpoint shift' }
            71 { return 'YES      BC1 endpoint shift (sRGB)' }
            72 { return 'YES      BC2 endpoint shift' }
            73 { return 'YES      BC2 endpoint shift (sRGB)' }
            74 { return 'YES      BC3 endpoint shift' }
            75 { return 'YES      BC3 endpoint shift (sRGB)' }
            76 { return 'NO       BC4 single channel' }
            77 { return 'NO       BC4 single channel' }
            78 { return 'NO       BC5 normal map' }
            79 { return 'NO       BC5 normal map' }
            80 { return 'NO       BC6H not endpoint-addressable' }
            81 { return 'NO       BC6H not endpoint-addressable' }
            82 { return 'NO       BC7 not endpoint-addressable' }
            83 { return 'NO       BC7 not endpoint-addressable' }
            default { return 'NO       unmapped DXGI format' }
        }
    }
    switch ($Row.FourCC) {
        'DXT1' { return 'YES      BC1 endpoint shift' }
        'DXT3' { return 'YES      BC2 endpoint shift' }
        'DXT5' { return 'YES      BC3 endpoint shift' }
        'DXT2' { return 'PARTIAL  premultiplied BC2 variant' }
        'DXT4' { return 'PARTIAL  premultiplied BC3 variant' }
        'BC4U' { return 'NO       BC4 single channel' }
        'BC4S' { return 'NO       BC4 single channel' }
        'ATI1' { return 'NO       BC4-family single channel' }
        'BC5U' { return 'NO       BC5 normal map' }
        'BC5S' { return 'NO       BC5 normal map' }
        'ATI2' { return 'NO       BC5 normal map' }
        default { return 'NO       unrecognised or uncompressed' }
    }
}

function Write-Histogram {
    param(
        [string]$Title,
        $Items,
        [int]$Top = 0,
        [scriptblock]$Label
    )
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
    $grouped = @($Items | Group-Object | Sort-Object Count -Descending)
    if ($Top -gt 0) { $grouped = @($grouped | Select-Object -First $Top) }
    if ($grouped.Count -eq 0) { Write-Host '  (none)'; return }
    foreach ($g in $grouped) {
        $text = if ($Label) { & $Label $g.Name } else { $g.Name }
        Write-Host ("  {0,7}  {1}" -f $g.Count, $text)
    }
}

function Format-HexDump {
    param([byte[]]$Bytes, [int]$Width = 16)
    $lines = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $Bytes.Length; $i += $Width) {
        $end   = [Math]::Min($i + $Width - 1, $Bytes.Length - 1)
        $slice = $Bytes[$i..$end]
        $hex   = (($slice | ForEach-Object { '{0:X2}' -f $_ }) -join ' ').PadRight(47)
        $ascii = -join ($slice | ForEach-Object {
            if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { '.' }
        })
        $lines.Add(('  {0:X4}  {1}  {2}' -f $i, $hex, $ascii))
    }
    return $lines
}

# ---------------------------------------------------------------------------
# Locate files.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host 'DDS corpus survey' -ForegroundColor Cyan
Write-Host "  root: $Root"

if (-not (Test-Path -LiteralPath $Root)) {
    Write-Host '  ERROR: root folder does not exist.' -ForegroundColor Red
    exit 2
}

$rootItem = Get-Item -LiteralPath $Root
if (-not $rootItem.PSIsContainer) {
    Write-Host '  ERROR: root is not a folder.' -ForegroundColor Red
    exit 2
}

# Absolute, normalised, no trailing separator, so relative-path math is reliable.
$rootFull = $rootItem.FullName.TrimEnd('\', '/')

$files = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -ieq '.dds' })

Write-Host "  files: $($files.Count)"

if ($files.Count -eq 0) {
    Write-Host 'No .dds files found.' -ForegroundColor Yellow
    exit 1
}

# ---------------------------------------------------------------------------
# Pass 1 - parse every header.
# ---------------------------------------------------------------------------
$rows      = New-Object System.Collections.Generic.List[object]
$anomalies = New-Object System.Collections.Generic.List[string]

$index = 0
foreach ($f in $files) {
    $index++
    if (-not $Quiet -and ($index % 2000 -eq 0)) {
        Write-Host "  ... $index / $($files.Count)" -ForegroundColor DarkGray
    }

    try {
        $buf = New-Object byte[] $HEADER_BYTES_DX10
        $fs = [System.IO.File]::Open($f.FullName, [System.IO.FileMode]::Open,
                                     [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try   { $read = $fs.Read($buf, 0, $HEADER_BYTES_DX10) }
        finally { $fs.Dispose() }
    } catch {
        $anomalies.Add("$($f.FullName): could not read ($($_.Exception.Message))")
        continue
    }

    if ($read -lt $HEADER_BYTES_BASE) {
        $anomalies.Add("$($f.FullName): only $read bytes, shorter than a DDS header")
        continue
    }

    $magic = [System.Text.Encoding]::ASCII.GetString($buf, 0, 4)
    if ($magic -ne 'DDS ') {
        $anomalies.Add("$($f.FullName): bad magic '$magic'")
        continue
    }

    $headerSize = [BitConverter]::ToUInt32($buf, 4)
    if ($headerSize -ne 124) {
        $anomalies.Add("$($f.FullName): dwSize is $headerSize (expected 124)")
    }

    $dwFlags = [BitConverter]::ToUInt32($buf, 8)
    $height  = [BitConverter]::ToUInt32($buf, 12)
    $width   = [BitConverter]::ToUInt32($buf, 16)
    $pitch   = [BitConverter]::ToUInt32($buf, 20)
    $depth   = [BitConverter]::ToUInt32($buf, 24)
    $rawMips = [BitConverter]::ToUInt32($buf, 28)

    $pfSize  = [BitConverter]::ToUInt32($buf, 76)
    $pfFlags = [BitConverter]::ToUInt32($buf, 80)
    $fourCc  = [System.Text.Encoding]::ASCII.GetString($buf, 84, 4)
    $pfBits  = [BitConverter]::ToUInt32($buf, 88)
    $maskR   = [BitConverter]::ToUInt32($buf, 92)
    $maskG   = [BitConverter]::ToUInt32($buf, 96)
    $maskB   = [BitConverter]::ToUInt32($buf, 100)
    $maskA   = [BitConverter]::ToUInt32($buf, 104)

    $caps  = [BitConverter]::ToUInt32($buf, 108)
    $caps2 = [BitConverter]::ToUInt32($buf, 112)
    $caps3 = [BitConverter]::ToUInt32($buf, 116)
    $caps4 = [BitConverter]::ToUInt32($buf, 120)
    $fccLabel = Get-FourCcLabel $fourCc
    $isDx10   = ($fccLabel -eq 'DX10')

    $dxgi = $null; $resDim = $null; $miscFlag = $null; $arraySize = $null; $misc2 = $null

    if ($isDx10) {
        if ($read -lt $HEADER_BYTES_DX10) {
            $anomalies.Add("$($f.FullName): DX10 FourCC but only $read bytes (need $HEADER_BYTES_DX10)")
            continue
        }
        $dxgi      = [BitConverter]::ToUInt32($buf, 128)
        $resDim    = [BitConverter]::ToUInt32($buf, 132)
        $miscFlag  = [BitConverter]::ToUInt32($buf, 136)
        $arraySize = [BitConverter]::ToUInt32($buf, 140)
        $misc2     = [BitConverter]::ToUInt32($buf, 144)
    }

    $mips = if ($rawMips -eq 0) { 1 } else { [int]$rawMips }

    $fullChain = 0
    if ($width -gt 0 -and $height -gt 0) {
        $fullChain = [int][Math]::Floor([Math]::Log([Math]::Max($width, $height), 2)) + 1
    }

    # Resolve block/bpp so we can validate the payload size.
    $blockBytes = 0
    $bytesPerPixel = 0
    if ($isDx10) {
        $blockBytes = Get-BlockBytesFromDxgi -DxgiFormat ([int]$dxgi)
        if ($blockBytes -eq 0) {
            $bytesPerPixel = Get-BytesPerPixelFromDxgi -DxgiFormat ([int]$dxgi)
        }
    } elseif ($FOURCC_BLOCK_BYTES.ContainsKey($fccLabel)) {
        $blockBytes = $FOURCC_BLOCK_BYTES[$fccLabel]
    } elseif ($pfBits -gt 0) {
        $bytesPerPixel = [int]($pfBits / 8)
    }

    $headerBytes = if ($isDx10) { $HEADER_BYTES_DX10 } else { $HEADER_BYTES_BASE }

    $expectedSize = $null
    if ($blockBytes -gt 0 -or $bytesPerPixel -gt 0) {
        $sum = 0
        for ($i = 0; $i -lt $mips; $i++) {
            $sum += Get-MipLevelSizeBytes -Width ([int]$width) -Height ([int]$height) `
                                          -BlockBytes $blockBytes -BytesPerPixel $bytesPerPixel -MipIndex $i
        }
        $expectedSize = [long]$headerBytes + [long]$sum
    }

    $rows.Add([pscustomobject]@{
        Path          = $f.FullName
        Name          = $f.Name
        Ext           = $f.Extension
        Size          = [long]$f.Length
        FourCC        = $fccLabel
        DxgiFormat    = $dxgi
        DxgiName      = if ($null -ne $dxgi) { $DXGI_NAMES[[int]$dxgi] } else { $null }
        ResourceDim   = $resDim
        ArraySize     = $arraySize
        MiscFlag      = $miscFlag
        MiscFlags2    = $misc2
        Width         = [int]$width
        Height        = [int]$height
        Depth         = [int]$depth
        Mips          = $mips
        RawMips       = [int]$rawMips
        FullChain     = $fullChain
        Caps          = $caps
        Caps2         = $caps2
        Caps3         = $caps3
        Caps4         = $caps4
        PfFlags       = $pfFlags
        PfSize        = $pfSize
        Bits          = [int]$pfBits
        MaskR         = $maskR
        MaskG         = $maskG
        MaskB         = $maskB
        MaskA         = $maskA
        PitchOrLinear = $pitch
        HdrFlags      = $dwFlags
        HeaderBytes   = $headerBytes
        ExpectedSize  = $expectedSize
        SizeMatches   = if ($null -ne $expectedSize) { ([long]$f.Length -eq [long]$expectedSize) } else { $null }
        BlockBytes    = $blockBytes
    })
}

# ---------------------------------------------------------------------------
# Pass 2 - aggregate.
# ---------------------------------------------------------------------------
$parsed  = $rows.Count
$skipped = $files.Count - $parsed

Write-Host ""
Write-Host '=== TOTALS ===' -ForegroundColor Cyan
Write-Host ("  {0,7}  .dds files discovered (recursive)" -f $files.Count)
Write-Host ("  {0,7}  headers parsed" -f $parsed)
if ($skipped -gt 0) {
    Write-Host ("  {0,7}  skipped as anomalous" -f $skipped) -ForegroundColor Yellow
}

$dirs = @($files | ForEach-Object { $_.DirectoryName } | Sort-Object -Unique)
$depth = 0
foreach ($d in $dirs) {
    $rel  = $d.Substring([Math]::Min($d.Length, $rootFull.Length))
    $n    = @($rel -split '[\\/]' | Where-Object { $_ }).Count
    if ($n -gt $depth) { $depth = $n }
}

Write-Host ""
Write-Host '=== DIRECTORY SHAPE ===' -ForegroundColor Cyan
Write-Host ("  {0,7}  distinct folders" -f $dirs.Count)
Write-Host ("  {0,7}  max folder depth below root" -f $depth)

Write-Host ""
Write-Host '=== FILE EXTENSION CASE ===' -ForegroundColor Cyan
foreach ($g in @($rows | Group-Object Ext | Sort-Object Count -Descending)) {
    Write-Host ("  {0,7}  {1}" -f $g.Count, $g.Name)
}

Write-Histogram -Title 'FOURCC' -Items ($rows | Select-Object -ExpandProperty FourCC)

Write-Host ""
Write-Host '=== DXGI FORMAT (DX10 files only) ===' -ForegroundColor Cyan
$dx10Rows = @($rows | Where-Object { $null -ne $_.DxgiFormat })
if ($dx10Rows.Count -eq 0) {
    Write-Host '  (no DX10 files)'
} else {
    foreach ($g in @($dx10Rows | Group-Object DxgiFormat | Sort-Object Count -Descending)) {
        $id   = [int]$g.Name
        $name = if ($DXGI_NAMES.ContainsKey($id)) { $DXGI_NAMES[$id] } else { '<UNMAPPED>' }
        Write-Host ("  {0,7}  dxgi={1,-4} {2}" -f $g.Count, $id, $name)
    }
}

Write-Histogram -Title 'RESOLUTION (WxH)' -Top 40 `
    -Items ($rows | ForEach-Object { "$($_.Width)x$($_.Height)" })

Write-Host ""
Write-Host '=== MIPMAP COUNT ===' -ForegroundColor Cyan
foreach ($g in @($rows | Group-Object Mips | Sort-Object { [int]$_.Name })) {
    Write-Host ("  {0,7}  mips={1}" -f $g.Count, $g.Name)
}

Write-Host ""
Write-Host '=== MIP CHAIN vs FULL CHAIN ===' -ForegroundColor Cyan
$full  = @($rows | Where-Object { $_.Mips -eq $_.FullChain })
$trunc = @($rows | Where-Object { $_.Mips -lt $_.FullChain })
$over  = @($rows | Where-Object { $_.Mips -gt $_.FullChain })
Write-Host ("  {0,7}  full chain (mips == floor(log2(max(w,h)))+1)" -f $full.Count)
Write-Host ("  {0,7}  truncated chain" -f $trunc.Count) -ForegroundColor $(if ($trunc.Count) { 'Yellow' } else { 'Gray' })
Write-Host ("  {0,7}  longer than full chain" -f $over.Count) -ForegroundColor $(if ($over.Count) { 'Yellow' } else { 'Gray' })
if ($trunc.Count -gt 0) {
    Write-Host '  truncated examples:'
    $trunc | Select-Object -First 10 | ForEach-Object {
        Write-Host ("    {0}  {1}x{2} mips={3} full={4}" -f $_.Name, $_.Width, $_.Height, $_.Mips, $_.FullChain)
    }
}

Write-Host ""
Write-Host '=== STRUCTURAL FEATURES ===' -ForegroundColor Cyan
$vol      = @($rows | Where-Object { $_.Depth -gt 1 })
$cube     = @($rows | Where-Object { ($_.Caps2 -band 0x200) -ne 0 })
$cubeFace = @($rows | Where-Object { ($_.Caps2 -band 0xFC00) -ne 0 })
$arr      = @($rows | Where-Object { $null -ne $_.ArraySize -and $_.ArraySize -gt 1 })
$uncomp   = @($rows | Where-Object { $_.Bits -ne 0 -and (($_.MaskR -bor $_.MaskG -bor $_.MaskB -bor $_.MaskA) -ne 0) })

function Write-Feature {
    param([string]$Key, [int]$Count)
    $colour = if ($Count -gt 0) { 'Yellow' } else { 'Gray' }
    Write-Host ("  {0,7}  {1}" -f $Count, $Key) -ForegroundColor $colour
}

Write-Feature 'volume textures (dwDepth > 1)'        $vol.Count
Write-Feature 'cube maps (caps2 & 0x200)'            $cube.Count
Write-Feature 'any cube face flag (caps2 & 0xFC00)'  $cubeFace.Count
Write-Feature 'texture arrays (arraySize > 1)'       $arr.Count
Write-Feature 'uncompressed RGBA masks'              $uncomp.Count

foreach ($set in @(
    @{ Name = 'volume';       Items = $vol },
    @{ Name = 'cube';         Items = $cube },
    @{ Name = 'array';        Items = $arr },
    @{ Name = 'uncompressed'; Items = $uncomp }
)) {
    if ($set.Items.Count -gt 0) {
        Write-Host "  $($set.Name) examples:"
        $set.Items | Select-Object -First 10 | ForEach-Object {
            Write-Host ("    {0}  {1} {2}x{3} mips={4}" -f $_.Name, $_.FourCC, $_.Width, $_.Height, $_.Mips)
        }
    }
}

Write-Host ""
Write-Host '=== SIZE MATH VALIDATION ===' -ForegroundColor Cyan
Write-Host '  formula: fileSize == headerBytes + sum over mip levels of'
Write-Host '             bw = max(1, ceil((w >> i) / 4))'
Write-Host '             bh = max(1, ceil((h >> i) / 4))'
Write-Host '             levelBytes = bw * bh * blockBytes     (block formats)'
Write-Host '             levelBytes = (w >> i) * (h >> i) * bpp (uncompressed)'
Write-Host ''

$checkable = @($rows | Where-Object { $null -ne $_.ExpectedSize })
$match     = @($checkable | Where-Object { $_.SizeMatches })
$mismatch  = @($checkable | Where-Object { -not $_.SizeMatches })
$uncheck   = @($rows | Where-Object { $null -eq $_.ExpectedSize })

Write-Host ("  {0,7}  checkable files" -f $checkable.Count)
Write-Host ("  {0,7}  size MATCHES" -f $match.Count) -ForegroundColor $(if ($mismatch.Count -eq 0) { 'Green' } else { 'Gray' })
Write-Host ("  {0,7}  size MISMATCH" -f $mismatch.Count) -ForegroundColor $(if ($mismatch.Count -gt 0) { 'Red' } else { 'Gray' })
Write-Host ("  {0,7}  not checkable (unknown format)" -f $uncheck.Count) -ForegroundColor $(if ($uncheck.Count -gt 0) { 'Yellow' } else { 'Gray' })

if ($mismatch.Count -gt 0) {
    Write-Host ''
    Write-Host '  first 25 mismatches:' -ForegroundColor Yellow
    foreach ($m in @($mismatch | Select-Object -First 25)) {
        Write-Host ("    {0}" -f $m.Name)
        Write-Host ("      actual={0} expected={1} diff={2}  {3} {4}x{5} mips={6}" -f `
            $m.Size, $m.ExpectedSize, ($m.Size - $m.ExpectedSize), $m.FourCC, $m.Width, $m.Height, $m.Mips)
    }
}

if ($uncheck.Count -gt 0) {
    Write-Host ''
    Write-Host '  not-checkable formats:' -ForegroundColor Yellow
    foreach ($g in @($uncheck | Group-Object FourCC | Sort-Object Count -Descending)) {
        Write-Host ("    {0,7}  {1}" -f $g.Count, $g.Name)
    }
}

Write-Host ''
Write-Host '=== ANOMALIES ===' -ForegroundColor Cyan
if ($anomalies.Count -eq 0) {
    Write-Host '  none'
} else {
    Write-Host ("  {0,7}  anomalous files" -f $anomalies.Count) -ForegroundColor Yellow
    $anomalies | Select-Object -First 40 | ForEach-Object { Write-Host "    $_" }
    if ($anomalies.Count -gt 40) { Write-Host ("    ... and {0} more" -f ($anomalies.Count - 40)) }
}

Write-Host ''
Write-Host '=== ENDPOINT HUE-SHIFT ELIGIBILITY (block engine) ===' -ForegroundColor Cyan
Write-Host '  Determines how much of the corpus the default (exact-size) engine can handle.'
Write-Host ''
$elig = New-Object System.Collections.Generic.List[object]
foreach ($r in $rows) {
    $elig.Add([pscustomobject]@{
        Fmt  = if ($null -ne $r.DxgiFormat) { "DXGI $($r.DxgiFormat)" } else { $r.FourCC }
        Verdict = Get-HueShiftEligibility -Row $r
    })
}
foreach ($g in @($elig | Group-Object Verdict | Sort-Object Count -Descending)) {
    Write-Host ("  {0,7}  {1}" -f $g.Count, $g.Name)
}

$yesCount = @($elig | Where-Object { $_.Verdict -like 'YES*' }).Count
$pct = if ($rows.Count -gt 0) { [Math]::Round(100.0 * $yesCount / $rows.Count, 1) } else { 0 }
Write-Host ''
Write-Host ("  endpoint-engine coverage: {0} / {1} files ({2}%)" -f $yesCount, $rows.Count, $pct) `
    -ForegroundColor $(if ($pct -ge 90) { 'Green' } elseif ($pct -ge 50) { 'Yellow' } else { 'Red' })

if ($Csv) {
    $csvFull = [System.IO.Path]::GetFullPath($Csv)
    $rows | Export-Csv -LiteralPath $csvFull -NoTypeInformation -Encoding UTF8
    Write-Host ''
    Write-Host "=== CSV EXPORTED ===" -ForegroundColor Cyan
    Write-Host "  $csvFull"
}

# ---------------------------------------------------------------------------
# Representative header dumps.
# ---------------------------------------------------------------------------
if ($DumpHeaders -gt 0) {
    Write-Host ''
    Write-Host '=== REPRESENTATIVE HEADER DUMPS ===' -ForegroundColor Cyan

    $picked = New-Object System.Collections.Generic.List[object]
    $seen   = New-Object System.Collections.Generic.HashSet[string]

    $buckets = @($rows | Group-Object {
        if ($null -ne $_.DxgiFormat) { "DXGI:$($_.DxgiFormat)" } else { "FOURCC:$($_.FourCC)" }
    })

    foreach ($b in $buckets) {
        if ($picked.Count -ge $DumpHeaders) { break }
        foreach ($r in @($b.Group | Select-Object -First $SamplePerFormat)) {
            if ($picked.Count -ge $DumpHeaders) { break }
            if ($seen.Add($r.Path)) { $picked.Add($r) }
        }
    }
    foreach ($r in $rows) {
        if ($picked.Count -ge $DumpHeaders) { break }
        if ($seen.Add($r.Path)) { $picked.Add($r) }
    }

    foreach ($r in $picked) {
        $all   = [System.IO.File]::ReadAllBytes($r.Path)
        $count = [Math]::Min($r.HeaderBytes, $all.Length)
        $head  = $all[0..($count - 1)]

        Write-Host ''
        Write-Host ("--- {0}  ({1:N1} KB) ---" -f $r.Name, ($r.Size / 1KB)) -ForegroundColor White

        foreach ($line in (Format-HexDump -Bytes $head)) { Write-Host $line }

        Write-Host ''
        Write-Host '  decoded:'
        Write-Host ("    dwHeight         {0}" -f $r.Height)
        Write-Host ("    dwWidth          {0}" -f $r.Width)
        Write-Host ("    dwDepth          {0}" -f $r.Depth)
        Write-Host ("    dwMipMapCount    {0}  (raw {1}, full chain {2})" -f $r.Mips, $r.RawMips, $r.FullChain)
        Write-Host ("    dwPitchOrLinear  {0}" -f $r.PitchOrLinear)
        Write-Host ("    dwFlags          0x{0:X8}" -f $r.HdrFlags)
        Write-Host ("    dwCaps           0x{0:X8}" -f $r.Caps)
        Write-Host ("    dwCaps2          0x{0:X8}" -f $r.Caps2)
        Write-Host ("    dwCaps3          0x{0:X8}" -f $r.Caps3)
        Write-Host ("    dwCaps4          0x{0:X8}" -f $r.Caps4)
        Write-Host ("    pf.dwFlags       0x{0:X8}" -f $r.PfFlags)
        Write-Host ("    pf.dwRGBBitCount {0}" -f $r.Bits)
        Write-Host ("    pf masks         R=0x{0:X8} G=0x{1:X8} B=0x{2:X8} A=0x{3:X8}" -f `
            $r.MaskR, $r.MaskG, $r.MaskB, $r.MaskA)
        Write-Host ("    FourCC           {0}" -f $r.FourCC)

        if ($null -ne $r.DxgiFormat) {
            Write-Host ("    DX10 dxgiFormat  {0} ({1})" -f $r.DxgiFormat, $r.DxgiName)
            Write-Host ("    DX10 resDim      {0}" -f $r.ResourceDim)
            Write-Host ("    DX10 miscFlag    0x{0:X8}" -f $r.MiscFlag)
            Write-Host ("    DX10 arraySize   {0}" -f $r.ArraySize)
            Write-Host ("    DX10 miscFlags2  0x{0:X8}" -f $r.MiscFlags2)
        }

        Write-Host ("    blockBytes       {0}" -f $r.BlockBytes)
        Write-Host ("    expected size    {0}   actual {1}   match {2}" -f `
            $r.ExpectedSize, $r.Size, $r.SizeMatches)
        Write-Host ("    hue shift        {0}" -f (Get-HueShiftEligibility -Row $r))
    }
}

Write-Host ''
Write-Host 'Survey complete.' -ForegroundColor Cyan
exit 0
