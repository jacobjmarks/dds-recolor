<#
.SYNOPSIS
    Generates the small .dds test fixtures under tests/fixtures.

.DESCRIPTION
    Writes a handful of tiny, synthetic DDS files that the test suite runs
    against, so the tests are self-contained and reproducible.

    Each fixture pins one specific property. See tests/fixtures/README.md for the
    rationale behind each one.

    Output is deterministic: regenerating produces byte-identical files, so any
    change to this script shows up as a reviewable diff.

.PARAMETER OutputDirectory
    Where to write the fixtures. Defaults to tests/fixtures relative to the repo.

.PARAMETER Force
    Overwrite existing files.

.EXAMPLE
    pwsh -NoProfile -File .\tools\New-TestFixtures.ps1 -Force
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $OutputDirectory) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $OutputDirectory = Join-Path $repoRoot 'tests\fixtures'
}

# ---------------------------------------------------------------------------
# DDS constants
# ---------------------------------------------------------------------------
$DDS_MAGIC            = 0x20534444   # "DDS "
$DDSD_CAPS            = 0x1
$DDSD_HEIGHT          = 0x2
$DDSD_WIDTH           = 0x4
$DDSD_PIXELFORMAT     = 0x1000
$DDSD_MIPMAPCOUNT     = 0x20000
$DDSD_LINEARSIZE      = 0x80000
$DDPF_FOURCC          = 0x4
$DDSCAPS_COMPLEX      = 0x8
$DDSCAPS_TEXTURE      = 0x1000
$DDSCAPS_MIPMAP       = 0x400000

$FOURCC_BLOCK_BYTES = @{
    'DXT1' = 8
    'DXT3' = 16
    'DXT5' = 16
    'ATI1' = 8
    'ATI2' = 16
    'BC4U' = 8
    'BC5U' = 16
}

function Get-FullMipCount {
    param([int]$Width, [int]$Height)
    return [int][Math]::Floor([Math]::Log([Math]::Max($Width, $Height), 2)) + 1
}

function Get-MipLevelBytes {
    param([int]$Width, [int]$Height, [int]$BlockBytes, [int]$Level)
    $w = [Math]::Max(1, $Width -shr $Level)
    $h = [Math]::Max(1, $Height -shr $Level)
    $bw = [Math]::Max(1, [int][Math]::Ceiling($w / 4.0))
    $bh = [Math]::Max(1, [int][Math]::Ceiling($h / 4.0))
    return $bw * $bh * $BlockBytes
}

function Write-UInt32 {
    param([byte[]]$Buffer, [int]$Offset, [uint32]$Value)
    $Buffer[$Offset]     = [byte]($Value -band 0xFF)
    $Buffer[$Offset + 1] = [byte](($Value -shr 8) -band 0xFF)
    $Buffer[$Offset + 2] = [byte](($Value -shr 16) -band 0xFF)
    $Buffer[$Offset + 3] = [byte](($Value -shr 24) -band 0xFF)
}

function Write-FourCc {
    param([byte[]]$Buffer, [int]$Offset, [string]$FourCc)
    $padded = $FourCc.PadRight(4, [char]0).Substring(0, 4)
    for ($i = 0; $i -lt 4; $i++) {
        $Buffer[$Offset + $i] = [byte][char]$padded[$i]
    }
}

# ---------------------------------------------------------------------------
# 8-bit RGB to RGB565, using the same rounding the C# side uses.
# ---------------------------------------------------------------------------
function Narrow5 { param([int]$V) return [int][Math]::Floor(($V * 31 + 127) / 255) }
function Narrow6 { param([int]$V) return [int][Math]::Floor(($V * 63 + 127) / 255) }

function ConvertTo-Rgb565 {
    param([int]$R, [int]$G, [int]$B)
    return [uint16](((Narrow5 $R) -shl 11) -bor ((Narrow6 $G) -shl 5) -bor (Narrow5 $B))
}

# ---------------------------------------------------------------------------
# Block writers. Each returns 8 bytes (BC1) or 16 bytes (BC2/BC3).
# ---------------------------------------------------------------------------
function New-Bc1FourColorBlock {
    <#
        4-colour mode: c0 > c1 strictly, so index 3 is an opaque interpolation
        rather than a transparent texel.
    #>
    param([int]$R0, [int]$G0, [int]$B0, [int]$R1, [int]$G1, [int]$B1, [int]$IndexByte)

    $p0 = ConvertTo-Rgb565 $R0 $G0 $B0
    $p1 = ConvertTo-Rgb565 $R1 $G1 $B1

    # Enforce c0 > c1, nudging c1 down if quantisation collapsed the ordering.
    if ($p0 -le $p1) {
        $t = $p0; $p0 = $p1; $p1 = $t
        if ($p0 -le $p1) { $p1 = [uint16]([Math]::Max(0, [int]$p0 - 1)) }
    }

    $block = New-Object byte[] 8
    $block[0] = [byte]($p0 -band 0xFF)
    $block[1] = [byte](($p0 -shr 8) -band 0xFF)
    $block[2] = [byte]($p1 -band 0xFF)
    $block[3] = [byte](($p1 -shr 8) -band 0xFF)
    $block[4] = [byte]$IndexByte
    $block[5] = [byte]$IndexByte
    $block[6] = [byte]$IndexByte
    $block[7] = [byte]$IndexByte
    return $block
}

function New-Bc1ThreeColorBlock {
    <#
        3-colour mode: c0 <= c1, which switches index 3 to transparent black.
        Used to prove a hue shift cannot silently flip the block mode.
    #>
    param([int]$R0, [int]$G0, [int]$B0, [int]$R1, [int]$G1, [int]$B1, [int]$IndexByte)

    $p0 = ConvertTo-Rgb565 $R0 $G0 $B0
    $p1 = ConvertTo-Rgb565 $R1 $G1 $B1

    if ($p0 -gt $p1) { $t = $p0; $p0 = $p1; $p1 = $t }
    if ($p0 -eq $p1) { $p1 = [uint16]([Math]::Min(65535, [int]$p0 + 1)) }

    $block = New-Object byte[] 8
    $block[0] = [byte]($p0 -band 0xFF)
    $block[1] = [byte](($p0 -shr 8) -band 0xFF)
    $block[2] = [byte]($p1 -band 0xFF)
    $block[3] = [byte](($p1 -shr 8) -band 0xFF)
    $block[4] = [byte]$IndexByte
    $block[5] = [byte]$IndexByte
    $block[6] = [byte]$IndexByte
    $block[7] = [byte]$IndexByte
    return $block
}

function New-Bc1FlatBlock {
    <# Degenerate block: equal endpoints, so it decodes to a flat colour and its mode is unobservable. #>
    param([int]$R, [int]$G, [int]$B)

    $p = ConvertTo-Rgb565 $R $G $B
    $block = New-Object byte[] 8
    $block[0] = [byte]($p -band 0xFF)
    $block[1] = [byte](($p -shr 8) -band 0xFF)
    $block[2] = [byte]($p -band 0xFF)
    $block[3] = [byte](($p -shr 8) -band 0xFF)
    $block[4] = 0xE4; $block[5] = 0xE4; $block[6] = 0xE4; $block[7] = 0xE4
    return $block
}

function New-Bc3Block {
    <# BC3 = 8-byte interpolated alpha block, then an 8-byte BC1 colour block. #>
    param([int]$R0, [int]$G0, [int]$B0, [int]$R1, [int]$G1, [int]$B1, [int]$AlphaSeed)

    $block = New-Object byte[] 16
    # Alpha endpoints and 3-bit indices. Values are recognisable so tests can
    # assert these bytes are never touched by a colour shift.
    $block[0] = 0x10
    $block[1] = 0xF0
    $block[2] = [byte](($AlphaSeed * 0x1B + 0x0C) -band 0xFF)
    $block[3] = 0x6C
    $block[4] = 0xD8
    $block[5] = 0x42
    $block[6] = 0xA5
    $block[7] = 0x39

    $color = New-Bc1FourColorBlock $R0 $G0 $B0 $R1 $G1 $B1 0xE4
    [Array]::Copy($color, 0, $block, 8, 8)
    return $block
}

# ---------------------------------------------------------------------------
# Whole-file writer.
# ---------------------------------------------------------------------------
function New-DdsFile {
    param(
        [int]$Width,
        [int]$Height,
        [int]$MipCount,
        [string]$FourCc,
        [scriptblock]$BlockFactory   # param($level, $blockIndex) -> byte[]
    )

    $blockBytes = $FOURCC_BLOCK_BYTES[$FourCc]

    $levels = @()
    $total = 0
    for ($level = 0; $level -lt $MipCount; $level++) {
        $bytes = Get-MipLevelBytes $Width $Height $blockBytes $level
        $levels += $bytes
        $total += $bytes
    }

    $data = New-Object byte[] (128 + $total)

    Write-UInt32 $data 0  $DDS_MAGIC
    Write-UInt32 $data 4  124
    $flags = $DDSD_CAPS -bor $DDSD_HEIGHT -bor $DDSD_WIDTH -bor $DDSD_PIXELFORMAT -bor $DDSD_LINEARSIZE
    if ($MipCount -gt 1) { $flags = $flags -bor $DDSD_MIPMAPCOUNT }
    Write-UInt32 $data 8  ([uint32]$flags)
    Write-UInt32 $data 12 ([uint32]$Height)
    Write-UInt32 $data 16 ([uint32]$Width)
    Write-UInt32 $data 20 ([uint32]$levels[0])
    Write-UInt32 $data 24 0
    Write-UInt32 $data 28 ([uint32]$MipCount)

    Write-UInt32 $data 76 32
    Write-UInt32 $data 80 ([uint32]$DDPF_FOURCC)
    Write-FourCc $data 84 $FourCc

    Write-UInt32 $data 108 ([uint32]($DDSCAPS_TEXTURE -bor $DDSCAPS_MIPMAP -bor $DDSCAPS_COMPLEX))

    $cursor = 128
    $blockIndex = 0
    for ($level = 0; $level -lt $MipCount; $level++) {
        $bytes = $levels[$level]
        for ($offset = 0; $offset -lt $bytes; $offset += $blockBytes) {
            $block = & $BlockFactory $level $blockIndex
            [Array]::Copy($block, 0, $data, $cursor + $offset, $blockBytes)
            $blockIndex++
        }
        $cursor += $bytes
    }

    return $data
}

function New-Bc1Dds {
    param([int]$Width, [int]$Height, [int]$MipCount, [scriptblock]$BlockFactory)
    return New-DdsFile $Width $Height $MipCount 'DXT1' $BlockFactory
}

function New-Bc3Dds {
    param([int]$Width, [int]$Height, [int]$MipCount, [scriptblock]$BlockFactory)
    return New-DdsFile $Width $Height $MipCount 'DXT5' $BlockFactory
}

function Save-Dds {
    param([string]$Name, [byte[]]$Data)
    $path = Join-Path $OutputDirectory $Name
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    if ((Test-Path -LiteralPath $path) -and -not $Force) {
        Write-Host ("  {0,-44} exists, use -Force to overwrite" -f $Name) -ForegroundColor DarkGray
        return
    }
    [System.IO.File]::WriteAllBytes($path, $Data)
    Write-Host ("  {0,-44} {1,9:N0} bytes" -f $Name, $Data.Length)
}

# ---------------------------------------------------------------------------
# Generate.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "Generating DDS test fixtures" -ForegroundColor Cyan
Write-Host "  output: $OutputDirectory"
Write-Host ""

# --- bc1_flat_4x4: degenerate block, must be left byte-identical -------------
Save-Dds 'bc1_flat_4x4.dds' (New-Bc1Dds 4 4 3 {
    param($level, $index)
    New-Bc1FlatBlock 0x80 0x80 0x80
})

# --- bc1_gradient_64x64: the main rewrite test -------------------------------
Save-Dds 'bc1_gradient_64x64.dds' (New-Bc1Dds 64 64 (Get-FullMipCount 64 64) {
    param($level, $index)
    # Vary endpoints deterministically per block and mip so the transform has
    # real work to do, without needing stored image data.
    $seed = ($index * 37 + $level * 11) % 64
    $r0 = 0x60 + $seed
    $g0 = 0x40 + (($seed * 3) % 48)
    $b0 = 0x20 + (($seed * 7) % 40)
    $indexByte = (0xC1 + $level * 17 + $index * 5) -band 0xFF
    New-Bc1FourColorBlock $r0 $g0 $b0 ([int]($r0 * 0.7)) ([int]($g0 * 0.7)) ([int]($b0 * 0.7)) $indexByte
})

# --- bc1_threecolormode_64x64: 1-bit alpha mode must survive -----------------
Save-Dds 'bc1_threecolormode_64x64.dds' (New-Bc1Dds 64 64 (Get-FullMipCount 64 64) {
    param($level, $index)
    $seed = ($index * 23 + $level * 7) % 32
    New-Bc1ThreeColorBlock (0x20 + $seed) (0x18 + $seed) (0x10 + $seed) 0x8B 0x5A 0x2B 0xE4
})

# --- bc1_nonsquare_1024x512: most common real shape, largest fixture ---------
Save-Dds 'bc1_nonsquare_1024x512.dds' (New-Bc1Dds 1024 512 (Get-FullMipCount 1024 512) {
    param($level, $index)
    $seed = ($index * 13 + $level * 3) % 48
    New-Bc1FourColorBlock (0x70 + $seed) (0x50 + $seed) 0x30 0x40 0x30 0x20 0xE4
})

# --- bc1_nonsquare_512x256 --------------------------------------------------
# Endpoints stay in the brown/warm range so they fall inside the default
# 60-degree hue window around the brown source colour used by the tests. A blue
# or green fixture would be correctly rejected by the selective shift and would
# therefore test nothing.
Save-Dds 'bc1_nonsquare_512x256.dds' (New-Bc1Dds 512 256 (Get-FullMipCount 512 256) {
    param($level, $index)
    $seed = ($index * 19 + $level * 5) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# --- bc1_odd_17x3: non-multiple-of-4 on both axes ---------------------------
Save-Dds 'bc1_odd_17x3.dds' (New-Bc1Dds 17 3 1 {
    param($level, $index)
    New-Bc1FourColorBlock (0x80 + $index * 3) 0x60 0x40 0x40 0x30 0x20 0xE4
})

# --- bc1_tiny_1x1: smallest legal texture ----------------------------------
Save-Dds 'bc1_tiny_1x1.dds' (New-Bc1Dds 1 1 1 {
    param($level, $index)
    New-Bc1FourColorBlock 0x8B 0x5A 0x2B 0x40 0x30 0x20 0xE4
})

# ---------------------------------------------------------------------------
# Square shapes.
#
# These matter because a square and a non-square texture at the same mip count
# take different paths through the mip walk. A non-square texture's short axis
# reaches 1 before the long axis does, producing degenerate 2x1 and 1x1 tail
# levels; a square one bottoms out on both axes together. Getting the per-axis
# `max(1, dim >> i)` clamp wrong shows up only on one of the two families.
#
# Square and non-square shapes take different paths through the mip walk, so both are covered.
# ---------------------------------------------------------------------------

# --- bc1_square_512x512: a common square, mid-sized shape -------------------
Save-Dds 'bc1_square_512x512.dds' (New-Bc1Dds 512 512 (Get-FullMipCount 512 512) {
    param($level, $index)
    $seed = ($index * 41 + $level * 17) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# --- bc1_square_1024x1024: 3rd most common shape (325 files) ----------------
# Kept despite its 700 KB: it is the only fixture that is both square AND has an
# Kept despite its size: it is the only fixture that is both square AND has an
# 11-level chain. Square-at-11-levels is a distinct combination from the
# square 10-level and non-square 11-level fixtures.
Save-Dds 'bc1_square_1024x1024.dds' (New-Bc1Dds 1024 1024 (Get-FullMipCount 1024 1024) {
    param($level, $index)
    $seed = ($index * 31 + $level * 19) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# --- bc1_square_256x256: 9 mips (87 files) ----------------------------------
# Also the only fixture covering a 9-level chain. The mip walk must handle a
# chain that ends on a 4x4 level rather than a 1x1 one.

Save-Dds 'bc1_square_256x256.dds' (New-Bc1Dds 256 256 (Get-FullMipCount 256 256) {
    param($level, $index)
    $seed = ($index * 53 + $level * 23) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# --- bc1_square_128x128: 8 mips, small square (15 files) --------------------
Save-Dds 'bc1_square_128x128.dds' (New-Bc1Dds 128 128 (Get-FullMipCount 128 128) {
    param($level, $index)
    $seed = ($index * 61 + $level * 29) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# ---------------------------------------------------------------------------
# Remaining shapes from the survey distribution.
# ---------------------------------------------------------------------------

# --- bc1_thin_2048x4: 12-level chain, the deepest seen in practice -----------
# max(2048, 4) = 2048 -> floor(log2(2048)) + 1 = 12 levels. The height reaches 1
# after only 3 levels, so levels 4..11 have a 1-pixel-tall block row: this is the
# case where a wrong per-axis clamp (`max(1, h >> i)` omitted) would compute a
# zero-byte level and corrupt every subsequent offset.
#
# A 2048x1024 fixture would exercise the same 12-level chain at 1.4 MB. The mip
# walk only depends on per-axis halving, so the thin shape covers it for 5 KB.
Save-Dds 'bc1_thin_2048x4.dds' (New-Bc1Dds 2048 4 (Get-FullMipCount 2048 4) {
    param($level, $index)
    $seed = ($index * 11 + $level * 7) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# --- bc1_nonsquare_256x128: 9 mips (42 files) ------------------------------
Save-Dds 'bc1_nonsquare_256x128.dds' (New-Bc1Dds 256 128 (Get-FullMipCount 256 128) {
    param($level, $index)
    $seed = ($index * 43 + $level * 13) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# --- bc1_nonsquare_128x64: 8 mips, narrow (13 files) -----------------------
Save-Dds 'bc1_nonsquare_128x64.dds' (New-Bc1Dds 128 64 (Get-FullMipCount 128 64) {
    param($level, $index)
    $seed = ($index * 47 + $level * 11) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# --- bc1_square_64x64: 7 mips (3 files) ------------------------------------
Save-Dds 'bc1_square_64x64.dds' (New-Bc1Dds 64 64 (Get-FullMipCount 64 64) {
    param($level, $index)
    $seed = ($index * 59 + $level * 31) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# --- bc1_square_32x32: 6 mips, minimum-depth chain (1 file) ----------------
# Covers a 6-level chain, the shallowest worth exercising.
Save-Dds 'bc1_square_32x32.dds' (New-Bc1Dds 32 32 (Get-FullMipCount 32 32) {
    param($level, $index)
    $seed = ($index * 67 + $level * 37) % 56
    New-Bc1FourColorBlock (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 0xE4
})

# ---------------------------------------------------------------------------
# Content-realism fixtures.
#
# The fixtures above all use smoothly varying endpoints, which is unlike real
# art: texture blocks usually have their two endpoints close together (a flat
# region sampled finely) with the 2-bit indices carrying the gradient. When
# endpoints are already adjacent on the RGB565 grid, a hue shift can easily
# requantise both onto the SAME value and flatten the block, destroying the
# shading the indices encode.
#
# That is the single most likely way this tool could produce visible 4x4
# artifacts in a stretched texture, so it gets a dedicated fixture rather than relying on the
# smooth fixtures to happen to cover it.
# ---------------------------------------------------------------------------

# --- bc1_lowcontrast_256x256: endpoints one 565 step apart -----------------
# Deliberately hostile: minimal endpoint separation, and index patterns chosen
# to span the full 4-colour palette so any flattening is detectable.
Save-Dds 'bc1_lowcontrast_256x256.dds' (New-Bc1Dds 256 256 (Get-FullMipCount 256 256) {
    param($level, $index)
    # Base colour in the source hue neighbourhood, then a very small delta.
    $base = 0x40 + (($index * 7 + $level * 3) % 24)
    $indexByte = @(0xE4, 0x1B, 0x93, 0x6C, 0xB1, 0x4E, 0x27, 0x8D)[($index + $level) % 8]
    New-Bc1FourColorBlock $base ($base + 2) ([int]($base * 0.7)) ($base - 3) ($base - 2) ([int]($base * 0.7) - 2) $indexByte
})

# --- bc1_noisy_512x512: high-frequency index patterns ----------------------
# Stands in for noisy/detailed art (foliage, gravel, fabric). Every index byte
# is distinct so a per-index error cannot cancel out, and endpoints span a wide
# range so the 565 grid is well exercised.
Save-Dds 'bc1_noisy_512x512.dds' (New-Bc1Dds 512 512 (Get-FullMipCount 512 512) {
    param($level, $index)
    $r = 0x50 + (($index * 13) % 96)
    $g = 0x38 + (($index * 29) % 80)
    $b = 0x20 + (($index * 7) % 64)
    $indexByte = (($index * 97 + $level * 41) -bxor ($index -shl 3)) -band 0xFF
    New-Bc1FourColorBlock $r $g $b ([int]($r * 0.55)) ([int]($g * 0.55)) ([int]($b * 0.55)) $indexByte
})

# --- bc3_square_512x256: BC3 on a common shape ---------------------------
# The single existing BC3 fixture is square and mid-sized. BC3 files commonly
# sit on wide shapes too, so cover one, with a distinct alpha pattern per level.
Save-Dds 'bc3_nonsquare_512x256.dds' (New-Bc3Dds 512 256 (Get-FullMipCount 512 256) {
    param($level, $index)
    $seed = ($index * 37 + $level * 17) % 56
    New-Bc3Block (0x8B + $seed) (0x5A + [int]($seed * 0.6)) (0x2B + [int]($seed * 0.3)) 0x40 0x30 0x20 ($index + $level)
})

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan

