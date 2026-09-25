namespace DdsRecolor.Core.Formats;

/// <summary>
/// The block-compressed formats this tool understands.
/// </summary>
/// <remarks>
/// Scope is driven by what texture sets actually contain in practice: mostly
/// BC1, with a smaller number of BC3 files, almost always using legacy FourCC
/// headers rather than DX10 ones. BC2 is included because it is trivial (a BC1
/// colour block plus an explicit alpha block) and costs nothing to support.
///
/// BC4/BC5 are recognised only so the tool can refuse them explicitly rather
/// than corrupting normal maps. BC6H/BC7/BC8 are recognised so the tool can
/// report them as unsupported instead of guessing.
/// </remarks>
public enum DdsFormat
{
    Unknown = 0,

    /// <summary>DXT1 / BC1. 8 bytes per 4x4 block, two RGB565 endpoints.</summary>
    BC1,

    /// <summary>DXT3 / BC2. 16 bytes per block: explicit 4-bit alpha + BC1 colour block.</summary>
    BC2,

    /// <summary>DXT5 / BC3. 16 bytes per block: interpolated alpha + BC1 colour block.</summary>
    BC3,

    /// <summary>BC4 (ATI1 / BC4U / BC4S). Single channel, 8 bytes per block.</summary>
    BC4,

    /// <summary>BC5 (ATI2 / BC5U / BC5S). Two channels, typically a normal map.</summary>
    BC5,

    /// <summary>BC6H. HDR, not endpoint-addressable by this tool.</summary>
    BC6H,

    /// <summary>BC7. Not endpoint-addressable by this tool.</summary>
    BC7,
}

/// <summary>
/// Facts about a <see cref="DdsFormat"/> that the recolor engines need.
/// </summary>
public static class DdsFormatInfo
{
    /// <summary>Bytes occupied by one 4x4 block. Zero when the format is not block compressed.</summary>
    public static int BlockBytes(this DdsFormat format) => format switch
    {
        DdsFormat.BC1 => 8,
        DdsFormat.BC2 => 16,
        DdsFormat.BC3 => 16,
        DdsFormat.BC4 => 8,
        DdsFormat.BC5 => 16,
        DdsFormat.BC6H => 16,
        DdsFormat.BC7 => 16,
        _ => 0,
    };

    /// <summary>
    /// True when the format stores its colour as two RGB565 endpoints per block,
    /// which is what the block-level endpoint engine rewrites.
    /// </summary>
    public static bool IsEndpointHueShiftable(this DdsFormat format) =>
        format is DdsFormat.BC1 or DdsFormat.BC2 or DdsFormat.BC3;

    /// <summary>
    /// Byte offset of the BC1 colour sub-block inside a full block, or -1 when
    /// there is no such sub-block.
    /// </summary>
    /// <remarks>
    /// BC2 and BC3 both lead with an alpha block and then carry a BC1 colour
    /// block, so a hue shift touches only the tail of each block and leaves the
    /// alpha bytes bit-identical.
    /// </remarks>
    public static int ColorBlockOffset(this DdsFormat format) => format switch
    {
        DdsFormat.BC1 => 0,
        DdsFormat.BC2 => 8,
        DdsFormat.BC3 => 8,
        _ => -1,
    };

    /// <summary>True when the format carries meaningful alpha alongside colour.</summary>
    public static bool HasAlpha(this DdsFormat format) =>
        format is DdsFormat.BC2 or DdsFormat.BC3;

    /// <summary>Human-readable name, matching the DDS FourCC where one exists.</summary>
    public static string DisplayName(this DdsFormat format) => format switch
    {
        DdsFormat.BC1 => "BC1 (DXT1)",
        DdsFormat.BC2 => "BC2 (DXT3)",
        DdsFormat.BC3 => "BC3 (DXT5)",
        DdsFormat.BC4 => "BC4",
        DdsFormat.BC5 => "BC5",
        DdsFormat.BC6H => "BC6H",
        DdsFormat.BC7 => "BC7",
        _ => "unknown",
    };

    /// <summary>
    /// Map a legacy DDS FourCC to a format. Returns <see cref="DdsFormat.Unknown"/>
    /// for unrecognised values, including "DX10" (the real format is then in the
    /// DX10 extension header and must be resolved from the DXGI value instead).
    /// </summary>
    public static DdsFormat FromFourCc(string fourCc) => fourCc switch
    {
        "DXT1" => DdsFormat.BC1,
        "DXT2" => DdsFormat.BC2,
        "DXT3" => DdsFormat.BC2,
        "DXT4" => DdsFormat.BC3,
        "DXT5" => DdsFormat.BC3,
        "ATI1" => DdsFormat.BC4,
        "BC4U" => DdsFormat.BC4,
        "BC4S" => DdsFormat.BC4,
        "ATI2" => DdsFormat.BC5,
        "BC5U" => DdsFormat.BC5,
        "BC5S" => DdsFormat.BC5,
        _ => DdsFormat.Unknown,
    };

    /// <summary>
    /// Map a DXGI_FORMAT value to a format. Only the DDS-relevant subset is
    /// listed; anything else yields <see cref="DdsFormat.Unknown"/>.
    /// </summary>
    public static DdsFormat FromDxgiFormat(int dxgiFormat) => dxgiFormat switch
    {
        70 or 71 => DdsFormat.BC1,   // BC1_UNORM, BC1_UNORM_SRGB
        72 or 73 => DdsFormat.BC2,   // BC2_UNORM, BC2_UNORM_SRGB
        74 or 75 => DdsFormat.BC3,   // BC3_UNORM, BC3_UNORM_SRGB
        76 or 77 => DdsFormat.BC4,   // BC4_UNORM, BC4_SNORM
        78 or 79 => DdsFormat.BC5,   // BC5_UNORM, BC5_SNORM
        80 or 81 => DdsFormat.BC6H,  // BC6H_UF16, BC6H_SF16
        82 or 83 => DdsFormat.BC7,   // BC7_UNORM, BC7_UNORM_SRGB
        _ => DdsFormat.Unknown,
    };
}
