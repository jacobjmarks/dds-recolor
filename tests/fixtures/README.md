# Test fixtures

Small, committed `.dds` files that the test suite runs against.

**20 files, 1.74 MB** — a fraction of the 475 MB corpus they were modelled on,
covering every code path that corpus exercises.

## Why these exist

Tests must not depend on external assets being present. An earlier iteration of the
suite read the real 1481-file corpus directly; that made the tests un-runnable
elsewhere, took 2m51s, and tested nothing the fixtures below cannot test more
precisely. Those tests were removed.

## Design principle

Fixtures are chosen to cover **code paths**, not to reproduce the corpus's file
list. Where two shapes exercise identical logic, only the smaller one is shipped:
a 2048-wide 12-level mip chain is covered by an 8 KB `2048x4` texture rather than a
1.4 MB `2048x1024` one, because the mip walk only depends on per-axis halving.

The two deliberate omissions (`2048x1024`, `2048x2048`) are pinned by
`FixtureSet_CoversEveryDeepestMipChainShapeUsedByTheCorpus` and documented in
`FixtureSet_CoversEveryShapeFamilyAndMipDepthInTheCorpus`, so the reasoning is
verified rather than merely asserted in a comment.

## The set

Grouped by what each fixture pins. Frequency figures come from
`tools/Invoke-DdsSurvey.ps1` run against the corpus.

### Corpus-shaped fixtures

Chosen to mirror the surveyed resolution distribution. The two dominant shapes
account for 783 of 1481 files (52.9%).

| Fixture | Corpus count | Mips | Why |
|---------|-------------|------|-----|
| `bc1_nonsquare_1024x512` | 451 (30.5%) | 11 | Most common shape. Non-square with a degenerate 2x1 then 1x1 mip tail. |
| `bc1_square_512x512` | 332 (22.4%) | 10 | 2nd most common. Square 10-level chain. |
| `bc1_square_1024x1024` | 325 (21.9%) | 11 | 3rd most common. The only square 11-level fixture. |
| `bc1_nonsquare_512x256` | 187 (12.6%) | 10 | Non-square, degenerate tail. |
| `bc1_square_256x256` | 87 (5.9%) | 9 | Only fixture with a 9-level chain. |
| `bc1_nonsquare_256x128` | 42 (2.8%) | 9 | Non-square 9-level. |
| `bc1_thin_2048x4` | covers 2048-wide (1.6%) | 12 | Deepest chain in the corpus, at 8 KB instead of 1.4 MB. Forces the 1-pixel-tall tail. |
| `bc1_square_128x128` | 15 (1.0%) | 8 | Square 8-level. |
| `bc1_nonsquare_128x64` | 13 (0.9%) | 8 | Narrow non-square. |
| `bc1_square_64x64` | 3 (0.2%) | 7 | Square 7-level. |
| `bc1_square_32x32` | 1 (0.1%) | 6 | Shallowest chain in the corpus. |

### Content-realism fixtures

Real texture blocks usually have endpoints close together, with the 2-bit indices
carrying the gradient. This is the highest-risk case for artifacts, and the smooth
fixtures above do not cover it.

| Fixture | Why |
|---------|-----|
| `bc1_lowcontrast_256x256` | Endpoints only 2-3 RGB565 steps apart. A hue shift can easily requantise both onto the *same* value, flattening the block and destroying its gradient. The engine skips such blocks; this proves the guard holds. |
| `bc1_noisy_512x512` | High-frequency, distinct index bytes per block, wide endpoint range. Stands in for foliage/gravel/fabric. Also the worst measured decoded-pixel error (729 squared, about 27 per channel). |
| `bc1_gradient_64x64` | Smooth endpoints with a varying index pattern. The general-purpose rewrite test. |

### Boundary and mode fixtures

| Fixture | Why |
|---------|-----|
| `bc1_flat_4x4` | Equal endpoints. Mode is unobservable and there is no gradient to preserve, so the engine must leave it byte-identical. |
| `bc1_tiny_1x1` | Smallest legal texture. |
| `bc1_odd_17x3` | Non-multiple-of-4 on both axes. Level sizing must round up per axis. |
| `bc1_threecolormode_64x64` | `c0 <= c1`, which carries 1-bit alpha. A hue shift that flipped the ordering would make transparent texels opaque. |
| `bc3_alpha_128x128` | BC3 interpolated alpha. The alpha half must never be touched. |
| `bc3_nonsquare_512x256` | BC3 on a corpus-common shape, cross-checking that BC3 gets the same mip handling as BC1. |

## Coverage guarantees

Two tests keep the set honest:

- `FixtureSet_CoversEveryShapeFamilyAndMipDepthInTheCorpus` — asserts every mip
  count 6-12 is present, both square and non-square shapes exist, a
  non-multiple-of-4 dimension exists, and both BC1 and BC3 are covered. Trimming
  the set fails this rather than silently dropping coverage.
- `FixtureSet_StaysSmallEnoughToCommit` — 2 MB budget.

## Regenerating

```powershell
pwsh -NoProfile -File .\tools\New-TestFixtures.ps1 -Force
```

The generator is committed so the fixtures can be rebuilt and audited rather than
being opaque binaries. Regeneration is byte-identical (verified), so any change to
the script shows up as a reviewable diff.

## Adding a fixture

Two traps worth knowing:

1. **Endpoint colours must sit within the hue window of the test's source colour.**
   The tests use a brown source (`#8B5A2B`, hue about 29 degrees) with a 60 degree
   tolerance. A fixture whose colours are blue (hue 210) is *correctly* left
   untouched, so a test asserting "the transform ran" would fail, and one asserting
   only "no artifact" would pass while proving nothing. Pick endpoints near the
   source hue.
2. **Prefer the smallest shape that exercises the code path.** A fixture is not
   evidence just because it is large. See `bc1_thin_2048x4` replacing a 1.4 MB
   `2048x1024` one for the same 12-level chain.

## Content

These files are entirely synthetic: single-colour, gradient, low-contrast and
noise patterns generated by `tools/New-TestFixtures.ps1`, containing no
third-party content.
