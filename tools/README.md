# tools

Repository-maintained helpers. Not part of the shipped CLI.

## `New-TestFixtures.ps1`

Generates the small `.dds` files under `tests/fixtures` that the test suite runs
against, so the tests are self-contained and reproducible.

Output is deterministic: regenerating produces byte-identical files, so any change
to the script shows up as a reviewable diff. The generator is committed so the
fixtures can be audited rather than being opaque binaries.

### Usage

```powershell
# regenerate in place
pwsh -NoProfile -File .\tools\New-TestFixtures.ps1 -Force

# write elsewhere
pwsh -NoProfile -File .\tools\New-TestFixtures.ps1 -OutputDirectory C:\temp\fx -Force
```

### Parameters

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `-OutputDirectory` | `tests/fixtures` | Where to write the fixtures |
| `-Force` | off | Overwrite existing files |

See `tests/fixtures/README.md` for what each fixture pins and why.

## `Invoke-DdsSurvey.ps1`

Read-only survey of a folder of `.dds` texture files. It parses **only the DDS
header** of every file (128 bytes, +20 when a DX10 extension header is present),
so it is fast even on a large corpus, and it never writes to the surveyed folder.

Its purpose is to determine which BCn formats the recolor tool must support, and
to characterise the shape distribution that the test fixtures should mirror.

### Usage

```powershell
# scan the current directory
pwsh -NoProfile -File .\tools\Invoke-DdsSurvey.ps1

# scan elsewhere, with CSV export and more header dumps
pwsh -NoProfile -File .\tools\Invoke-DdsSurvey.ps1 `
    -Root 'D:\some\corpus' -Csv .\survey.csv -DumpHeaders 8
```

### Parameters

| Parameter | Default | Meaning |
|-----------|---------|---------|
| `-Root` | `.` (current directory) | Folder to scan, recursively |
| `-Csv` | *(none)* | Optional path to export the per-file table as CSV |
| `-DumpHeaders` | `5` | How many representative files to dump as annotated hex (`0` disables) |
| `-SamplePerFormat` | `1` | How many representatives to prefer per distinct format |
| `-Quiet` | off | Suppress the progress counter |

### What it reports

- Total file count, distinct folders, max depth
- FourCC histogram
- DXGI format histogram for DX10 files (with enum names)
- Resolution histogram
- `dwMipMapCount` histogram, and whether each chain is full, truncated or long
- Volume / cube map / texture array / uncompressed detection
- **Size-math validation**: whether `fileSize == headerBytes + sum(mipLevelSize)`
  for every file, using
  `levelBytes = max(1, ceil(dim >> i / 4)) * blockBytes` for block formats.
  A 100% match validates the mip offset math before the codec is written.
- Anomaly list (short files, bad magic, unparseable headers)
- Per-format eligibility for the block-level endpoint hue-shift engine
- Annotated hex dumps of representative headers

### Exit codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | No `.dds` files found under `-Root` |
| `2` | `-Root` missing, or not a folder |

### Last run

2026-09-25 against a 1481-file corpus (475 MB): **zero anomalies**, **100%
size-match**, 1476 BC1 + 5 BC3, all legacy FourCC, all full mip chains, no
cube/array/volume. The interpretation is in `../PLAN.md` section 5.

### Notes

- DXGI enum values are documented in `dxgiformat.h`. The script hardcodes the
  DDS-relevant subset; if a new corpus reports `<UNMAPPED>`, check that file.
- The size check deliberately covers only formats whose block size or bytes-per-
  pixel is known. Unknown formats are listed as "not checkable" rather than
  reported as mismatches.
- The script was developed on PowerShell 7 (`pwsh`). It avoids PS7-only syntax
  such as the ternary operator, so it should also run under Windows PowerShell 5.1.
