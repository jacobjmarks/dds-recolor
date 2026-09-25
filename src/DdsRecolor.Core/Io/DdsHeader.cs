namespace DdsRecolor.Core.Io;

using DdsRecolor.Core.Formats;

/// <summary>Raised when a DDS file cannot be parsed or is not safe to rewrite.</summary>
public sealed class DdsFormatException : Exception
{
    public DdsFormatException(string message) : base(message) { }
}

/// <summary>
/// A parsed DDS header: the fixed 124-byte header plus its optional DX10
/// extension, with enough derived data to locate every mip level.
/// </summary>
/// <remarks>
/// <para>
/// The original header bytes are retained verbatim. The writer never rebuilds a
/// header, it patches the block payload in place, so unknown, vendor-specific
/// and reserved fields survive untouched by construction.
/// </para>
/// <para>
/// Layout of the fixed header, all little-endian:
/// </para>
/// <code>
///   +0   4   magic "DDS "
///   +4   4   dwSize (must be 124)
///   +8   4   dwFlags
///   +12  4   dwHeight
///   +16  4   dwWidth
///   +20  4   dwPitchOrLinearSize
///   +24  4   dwDepth
///   +28  4   dwMipMapCount
///   +32  44  dwReserved1[11]
///   +76  4   ddspf.dwSize (must be 32)
///   +80  4   ddspf.dwFlags
///   +84  4   ddspf.dwFourCC
///   +88  4   ddspf.dwRGBBitCount
///   +92  4   ddspf.dwRBitMask
///   +96  4   ddspf.dwGBitMask
///   +100 4   ddspf.dwBBitMask
///   +104 4   ddspf.dwABitMask
///   +108 4   dwCaps
///   +112 4   dwCaps2
///   +116 4   dwCaps3
///   +120 4   dwCaps4
///   +124 4   dwReserved2
///   +128 20  DX10 extension, only when dwFourCC == "DX10"
/// </code>
/// </remarks>
public sealed class DdsHeader
{
    public const int BaseSize = 128;
    public const int Dx10ExtensionSize = 20;
    public const int Dx10Size = BaseSize + Dx10ExtensionSize;

    private const uint Magic = 0x20534444; // "DDS "

    /// <summary>Bytes per 4x4 block for this texture's format. Zero if not block compressed.</summary>
    public int BlockBytes { get; private init; }

    /// <summary>Total header length in bytes: 128, or 148 when a DX10 extension is present.</summary>
    public int HeaderBytes { get; private init; }

    public uint Flags { get; private init; }
    public int Width { get; private init; }
    public int Height { get; private init; }
    public int Depth { get; private init; }
    public uint PitchOrLinearSize { get; private init; }
    public int MipCount { get; private init; }
    public uint Caps { get; private init; }
    public uint Caps2 { get; private init; }
    public string FourCc { get; private init; } = string.Empty;

    /// <summary>True when the file uses a DX10 extension header.</summary>
    public bool IsDx10 { get; private init; }

    /// <summary>DXGI format value when <see cref="IsDx10"/>, otherwise null.</summary>
    public int? DxgiFormat { get; private init; }

    /// <summary>Array size from the DX10 extension, when present.</summary>
    public int ArraySize { get; private init; }

    /// <summary>The texture's block-compressed format.</summary>
    public Formats.DdsFormat Format { get; private init; }

    /// <summary>
    /// Byte offset and length of every mip level within the file.
    /// </summary>
    public IReadOnlyList<(int Offset, int Length)> MipLevels { get; private init; } = [];

    /// <summary>Total payload bytes across all mip levels.</summary>
    public int PayloadBytes { get; private init; }

    /// <summary>True when the format carries a cube map.</summary>
    public bool IsCubeMap => (Caps2 & 0x200) != 0;

    /// <summary>True when the format is a volume texture.</summary>
    public bool IsVolumeTexture => Depth > 1;

    /// <summary>Number of array slices or cube faces.</summary>
    public int SliceCount
    {
        get
        {
            if (IsCubeMap) return 6 * Math.Max(1, ArraySize);
            return Math.Max(1, ArraySize);
        }
    }

    /// <summary>Parse the header from the start of <paramref name="data"/>.</summary>
    /// <exception cref="DdsFormatException">The data is not a DDS file this tool can handle.</exception>
    public static DdsHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < BaseSize)
        {
            throw new DdsFormatException(
                $"File is {data.Length} bytes, shorter than the {BaseSize}-byte DDS header.");
        }

        uint magic = ReadUInt32(data, 0);
        if (magic != Magic)
        {
            throw new DdsFormatException("Missing 'DDS ' magic.");
        }

        uint headerSize = ReadUInt32(data, 4);
        if (headerSize != 124)
        {
            throw new DdsFormatException($"dwSize is {headerSize}, expected 124.");
        }

        int height = checked((int)ReadUInt32(data, 12));
        int width = checked((int)ReadUInt32(data, 16));
        if (width <= 0 || height <= 0)
        {
            throw new DdsFormatException($"Invalid dimensions {width}x{height}.");
        }

        uint pitchOrLinearSize = ReadUInt32(data, 20);
        int depth = checked((int)ReadUInt32(data, 24));
        if (depth == 0) depth = 1;

        uint rawMips = ReadUInt32(data, 28);
        int mipCount = rawMips == 0 ? 1 : checked((int)rawMips);

        uint pfSize = ReadUInt32(data, 76);
        if (pfSize != 32)
        {
            throw new DdsFormatException($"ddspf.dwSize is {pfSize}, expected 32.");
        }

        string fourCc = System.Text.Encoding.ASCII.GetString(data.Slice(84, 4));
        uint rgbBitCount = ReadUInt32(data, 88);

        bool isDx10 = fourCc == "DX10";
        int? dxgiFormat = null;
        int arraySize = 1;

        Formats.DdsFormat format;
        int headerBytes;

        if (isDx10)
        {
            if (data.Length < Dx10Size)
            {
                throw new DdsFormatException(
                    $"DX10 FourCC but only {data.Length} bytes, need {Dx10Size}.");
            }

            dxgiFormat = checked((int)ReadUInt32(data, 128));
            arraySize = checked((int)ReadUInt32(data, 140));
            if (arraySize == 0) arraySize = 1;

            format = Formats.DdsFormatInfo.FromDxgiFormat(dxgiFormat.Value);
            headerBytes = Dx10Size;
        }
        else
        {
            format = Formats.DdsFormatInfo.FromFourCc(fourCc);
            headerBytes = BaseSize;

            if (format == Formats.DdsFormat.Unknown && rgbBitCount != 0)
            {
                throw new DdsFormatException(
                    $"Uncompressed format (FourCC '{fourCc}', {rgbBitCount} bpp) is not supported.");
            }
        }

        if (format == Formats.DdsFormat.Unknown)
        {
            string detail = isDx10 ? $"DXGI format {dxgiFormat}" : $"FourCC '{fourCc}'";
            throw new DdsFormatException($"Unrecognised format: {detail}.");
        }

        int blockBytes = format.BlockBytes();

        // Reject the structural variants this tool does not walk correctly.
        // Failing loudly is better than silently writing mip data at the wrong
        // offsets, which would corrupt the texture.
        if (depth > 1)
        {
            throw new DdsFormatException("Volume textures (dwDepth > 1) are not supported.");
        }

        var mipLevels = new List<(int Offset, int Length)>(mipCount);
        int cursor = headerBytes;
        int total = 0;

        for (int level = 0; level < mipCount; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);

            int blocksWide = Math.Max(1, (levelWidth + 3) / 4);
            int blocksHigh = Math.Max(1, (levelHeight + 3) / 4);
            int levelBytes = blocksWide * blocksHigh * blockBytes;

            int sliceCount = isDx10 ? Math.Max(1, arraySize) : 1;
            if ((ReadUInt32(data, 112) & 0x200) != 0) sliceCount *= 6;

            for (int slice = 0; slice < sliceCount; slice++)
            {
                mipLevels.Add((cursor, levelBytes));
                cursor += levelBytes;
                total += levelBytes;
            }
        }

        return new DdsHeader
        {
            BlockBytes = blockBytes,
            HeaderBytes = headerBytes,
            Flags = ReadUInt32(data, 8),
            Width = width,
            Height = height,
            Depth = depth,
            PitchOrLinearSize = pitchOrLinearSize,
            MipCount = mipCount,
            Caps = ReadUInt32(data, 108),
            Caps2 = ReadUInt32(data, 112),
            FourCc = fourCc,
            IsDx10 = isDx10,
            DxgiFormat = dxgiFormat,
            ArraySize = arraySize,
            Format = format,
            MipLevels = mipLevels,
            PayloadBytes = total,
        };
    }

    /// <summary>Expected total file size in bytes for this header.</summary>
    public int ExpectedFileSize => HeaderBytes + PayloadBytes;

    /// <summary>Validate that the actual file length matches the expected size.</summary>
    /// <exception cref="DdsFormatException">The length does not match.</exception>
    public void ValidateFileSize(int actualLength)
    {
        if (actualLength != ExpectedFileSize)
        {
            throw new DdsFormatException(
                $"File size {actualLength} does not match the {ExpectedFileSize} bytes " +
                $"implied by the header ({HeaderBytes}-byte header + {PayloadBytes}-byte payload). " +
                "Refusing to rewrite, as the mip offset table would be wrong.");
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] |
               (data[offset + 1] << 8) |
               (data[offset + 2] << 16) |
               (data[offset + 3] << 24));
}
