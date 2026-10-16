# dds-recolor

> [!NOTE]
> Work in progress

Recolor `.dds` texture files by mapping a source colour to a target colour.

Designed for recolouring textures while preserving everything that matters:
exact file size, mip maps, block format, metadata, and the geometry of the
original image. Validated against a corpus of 1481 textures.

```
dds-recolor <folder> --source #8B5A2B --target #C0C0C0
```

## Why this is not a simple image transform

Naively decoding a texture, shifting its colours and re-encoding it produces
visible damage: blocky 4×4 artifacts, shifted edges, and a file that no longer
matches the original's size or format. Textures get stretched across surfaces
and viewed at oblique angles, so a small error becomes a visibly wrong result.

This tool avoids that by rewriting **only the two RGB565 colour endpoints inside
each compressed block**, leaving the 2-bit colour indices untouched. Because the
indices encode the block's internal spatial pattern, the geometry is preserved
bit-for-bit. Two consequences fall out for free:

| Requirement | How it is met |
|-------------|---------------|
| Exact file size | Only 4 bytes per block change. No block changes size, so the output length equals the input length by construction. |
| No new block-seam artifacts | The index bits are never modified, so the pattern inside every 4×4 block is bit-identical to the original. |
| Deterministic | Pure arithmetic over the source bytes. No threading, randomness, or GPU involved. |
| Metadata preserved | The header is never re-serialised. Unknown and vendor fields round-trip untouched. |
| Mip maps preserved | Every mip level is walked and recoloured at its own dimensions. |
| BCn format preserved | The format is never converted. Only endpoint values within the existing blocks change. |

## Requirements

- .NET 10 SDK (to build)

## Build and test

```powershell
dotnet build
dotnet test          # 350 tests, ~6 seconds, no external assets required
```

## Usage

```
dds-recolor <folder> --source <#rrggbb> --target <#rrggbb> [options]
```

### Required

| Option | Meaning |
|--------|---------|
| `<folder>` | Folder containing `.dds` files. Searched recursively. |
| `--source <colour>` | Colour to remap. |
| `--target <colour>` | Colour to remap toward. |
| `--target-hue-offset <deg>` | Alternative to `--target`: rotate the source hue by this many degrees. |

### Colour formats

Both `--source` and `--target` accept any of these. All are case-insensitive and
whitespace-tolerant.

| Form | Examples |
|------|----------|
| Hex | `#8B5A2B` `#8b5a2b` `8B5A2B` `0x8B5A2B` `#FA0` `#8B5A2BFF` |
| CSS named | `tomato` `sienna` `rebeccapurple` — all 148 CSS keywords |
| `rgb()` | `rgb(139, 90, 43)` `rgb(139 90 43)` `rgb(0.55 0.35 0.17)` `rgb(55% 35% 17%)` |
| `hsl()` | `hsl(29, 53%, 36%)` `hsl(29 0.53 0.36)` `hsl(0.08turn 53% 36%)` |
| `hsv()` | `hsv(29 53% 55%)` |
| `hwb()` | `hwb(29 17% 45%)` |
| `lab()` | `lab(45 18 32)` `lab(45% 18 32)` |
| Bare | `rgb 139 90 43` `hsl 29 53% 36%` (parentheses optional) |

Three things are auto-detected so values can be pasted from other tools:

- **rgb ranges.** If any channel exceeds 1, all three are read as 0–255;
  otherwise as 0–1. Percentages always mean 0–100.
- **hue units.** A bare number is degrees; `deg`, `grad`, `rad` and `turn` work,
  and hue wraps (`400deg` is `40deg`).
- **percentages.** `50%` and `0.5` are equivalent for saturation, value and
  lightness.

**Out-of-range values are rejected, not clamped.** A silently clamped
`rgb(300, 0, 0)` would look like a working command while producing a colour you
did not ask for. A typo in a colour name gets a suggestion:

```
$ dds-recolor <folder> --source #803333 --target tomatoe
error: 'tomatoe' is not a valid colour.
  Expected one of: #8B5A2B, #FA0, 8B5A2B, tomato, rgb(139,90,43), ...
  Did you mean: tomato?
```

Alpha is accepted only when fully opaque (`/ 1`, `/ 100%`, or an `FF` hex byte),
because the engine shifts colour and has no opacity channel to write.

### Output

| Option | Meaning |
|--------|---------|
| `--out <dir>` | Output root. Default `{folder}_recolored`. |
| `--in-place` | Overwrite originals atomically. Prompts first; `--yes` skips the prompt. |
| `--yes`, `-y` | Skip the `--in-place` confirmation prompts. |
| `--mode <block\|reencode\|both>` | Technique. Default `block`. `reencode` is not implemented yet. |

### Colour

| Option | Meaning |
|--------|---------|
| `--target-hue-offset <deg>` | Rotate the source hue by this many degrees instead of naming a target. Negative cools. |
| `--intensity <n>` | Master strength, scaling all three channels. Default `1.0`. `0` leaves the texture byte-identical. |
| `--retain-saturation` | Keep saturation from the source image instead of adjusting it to the target's. |
| `--retain-brightness` | Keep brightness from the source image instead of adjusting it to the target's. |
| `--saturation <0..1>` | How far to adjust saturation. Default `1.0`. |
| `--brightness <0..1>` | How far to adjust brightness. Default `1.0`. |
| `--alpha-mode <straight\|premultiplied>` | How alpha is encoded. Default `straight`. |
| `--tolerance <deg>` | Hue window around the source colour. Default `60`. |

### Filtering and performance

| Option | Meaning |
|--------|---------|
| `--filter <glob>` | Only process matching filenames, e.g. `*wood*`. |
| `--jobs <n>` | Parallel workers. Default: processor count. |
| `--verbose` | Per-file detail. |

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | One or more files failed |
| 2 | Invalid arguments |
| 3 | Requested mode not implemented |
| 4 | Aborted at a confirmation prompt |

## Examples

```powershell
# Preview nothing, write alongside originals
dds-recolor "D:\corpus\targets" --source #8B5A2B --target #C0C0C0

# Any colour syntax works for either colour
dds-recolor "D:\corpus\targets" --source "hsl(0 60% 35%)" --target tomato
dds-recolor "D:\corpus\targets" --source #803333 --target "lab(70 -10 25)"

# Relative change: 30 degrees warmer, no need to name a target
dds-recolor "D:\corpus\targets" --source #803333 --target-hue-offset 30

# Recolour in place, accepting the confirmation prompts
dds-recolor "D:\corpus\targets" --source #8B5A2B --target #C0C0C0 --in-place --yes

# Only the wood textures, half strength, into a specific folder
dds-recolor "D:\corpus\targets" --source #8B5A2B --target #C0C0C0 `
    --filter "*wood*" --intensity 0.5 --out "D:\corpus\wood_recolored"
```

## How the recolour works

One global set of adjustment factors is computed from `source` to `target` and
applied to every qualifying pixel:

```
pixel.H' = pixel.H + (target.H - source.H) * intensity
pixel.S' = pixel.S * (target.S / source.S) ^ saturationStrength
pixel.V' = pixel.V * (target.V / source.V) ^ brightnessStrength
```

**All three channels adjust by default**, because source and target colours
usually differ in saturation and brightness as much as in hue. See below.

Each factor is **shared across all pixels**, so relative differences between
texels survive: a pixel twice as saturated as its neighbour stays twice as
saturated. Shading is scaled, not flattened — that is what keeps geometry detail
intact when a texture is stretched across a surface.

Saturation is gated separately from hue, so greyscale pixels stay grey and no
colour cast bleeds into shadows or highlights.

Selective by default: pixels outside the `--tolerance` hue window are left alone.

### "My texture still looks the wrong colour"

A hue-only adjustment can never reach a target that differs mainly in
**saturation or brightness**. For example:

| | Hue | Saturation | Value |
|---|---|---|---|
| source `#803333` | 0° | 0.60 | 0.50 |
| target `#D89D83` | 18° | 0.39 | 0.85 |

That target is only 18° away in hue (5% of the range) but 21% away in saturation
and 35% away in brightness. Adjusting hue alone produces `#804B33` — a dark
saturated red that still reads as the original colour. Raising `--intensity` does
not help: it rotates the hue *past* the target, so the red goes orange rather
than peach.

This is why all three channels adjust by default. Confirming it:

```powershell
$ dds-recolor <folder> --source #803333 --target #D89D83
  saturation  : adjusting to target, strength 1
  brightness  : adjusting to target, strength 1
```

The source colour now maps exactly onto the target (`#803333` → `#D89D83`).

### Retaining a channel

Sometimes you *want* to keep the source's character. Use `--retain-saturation`
and/or `--retain-brightness` to pin a channel to the source image:

```powershell
# Change hue and saturation, but leave lighting untouched
dds-recolor <folder> --source #803333 --target #D89D83 --retain-brightness

# Pure hue rotation: change nothing but hue
dds-recolor <folder> --source #803333 --target #D89D83 --retain-saturation --retain-brightness
```

`--retain-brightness` is worth reaching for when a texture's shading carries
real information, since brightness encodes lighting and geometry.

Retaining a channel means the result will **not** match your target, so the tool
says so:

```
  NOTE: a retained channel carries most of the difference to
        your target, so the result will NOT match it:
          saturation gap    21% of range   adjusting
          brightness gap    35% of range   <-- RETAINED
```

For finer control, `--saturation 0.5` and `--brightness 0.5` dial a partial
adjustment, and `--intensity` scales everything at once.

## Safety

- **`--in-place` prompts before overwriting.** The originals are written via a
  temporary file and an atomic move, so an interrupted run cannot leave a
  half-written texture.
- **Repeat runs are detected.** The transform is a fixed rotation, so applying it
  twice rotates twice and never converges. A second in-place run over textures
  that already look recoloured warns and asks for confirmation. See the
  documented limitation in `RecolorDetector`.
- **Unsupported formats are copied through, never guessed at.** BC4/BC5
  (normal maps), BC6H, BC7 and uncompressed formats are refused and reported
  rather than mangled. BC5 in particular must not be hue-shifted: it stores
  normals, and shifting them corrupts lighting.

## Supported formats

| Format | Status |
|--------|--------|
| BC1 / DXT1 | Fully supported |
| BC2 / DXT3 | Supported |
| BC3 / DXT5 | Supported; the alpha half is never modified |
| BC4 | Recognised, refused (single channel, no colour to shift) |
| BC5 / ATI2 | Recognised, refused (normal map) |
| BC6H, BC7 | Recognised, refused (not endpoint-addressable) |
| Uncompressed RGBA | Recognised, refused |

Cube maps, texture arrays and volume textures are rejected loudly rather than
mis-walked. None are present in the target corpus.

## Interactive GUI

Launch the desktop tuner with:

```powershell
dotnet run --project src/DdsRecolor.Gui
```

Open a folder to recursively list DDS textures, or open a single file. The GUI
previews the recolored base mip for BC1, BC2 and BC3 textures as parameters
change. Hold **Hold to view original** for a before/after comparison. The
parameter field copies the source/target colors and non-default recolor options;
append the fragment to a CLI invocation with the desired folder and output
arguments. Other recognized DDS formats remain listed but cannot be previewed
or recolored.

## Project layout

```
src/DdsRecolor.Core/       library: header parser, BCn codecs, hue shift, block engine
  Formats/                 format enum, RGB/HSV, RGB565 codec, BC1 block codec
  Io/                      DDS header parse/validate, file IO, BC preview decoder
  Recolor/                 hue shift transform, block engine, repeat-run detector
src/DdsRecolor.Cli/        command line front end
src/DdsRecolor.Gui/        Avalonia desktop texture tuner
tests/DdsRecolor.Tests/    unit and fixture-based regression tests
tests/fixtures/            20 committed .dds fixtures (see its README)
tools/                     corpus survey and fixture generator
PLAN.md                    design decisions, measured results, and known risks
```

## Design notes

`PLAN.md` records the reasoning in detail, including:

- Why block-level endpoint rewriting is the default rather than decode/re-encode.
- The BC1 colour-mode hazard: a hue shift can flip which endpoint is numerically
  larger, silently turning a 4-colour block into a 3-colour-plus-transparent one.
  The engine detects this and swaps endpoints while inverting indices, which
  leaves the decoded image unchanged.
- Measured validation across the real corpus: 62 million blocks, zero size,
  header, mode or flattening failures, gradient spread changing by 0.4%.
- Two real bugs found during that validation, and one that was a false alarm.
