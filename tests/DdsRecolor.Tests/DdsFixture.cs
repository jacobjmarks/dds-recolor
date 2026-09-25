namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;

/// <summary>
/// Builds in-memory DDS files for tests.
/// </summary>
/// <remarks>
/// The real corpus is a useful smoke test, but tests need inputs with known
/// content: a specific block mode, a non-multiple-of-4 size, a truncated mip
/// chain, or a deliberately corrupt length. Those are constructed here so the
/// assertions can be exact rather than statistical.
/// </remarks>
public static class DdsFixture
{
    /// <summary>BC1 payload size for a given level, in bytes.</summary>
    public static int Bc1LevelBytes(int width, int height)
    {
        int blocksWide = Math.Max(1, (width + 3) / 4);
        int blocksHigh = Math.Max(1, (height + 3) / 4);
        return blocksWide * blocksHigh * 8;
    }

    /// <summary>BC3 payload size for a given level, in bytes.</summary>
    public static int Bc3LevelBytes(int width, int height)
    {
        int blocksWide = Math.Max(1, (width + 3) / 4);
        int blocksHigh = Math.Max(1, (height + 3) / 4);
        return blocksWide * blocksHigh * 16;
    }

    /// <summary>Total number of BC1 mip levels for a full chain.</summary>
    public static int FullMipCount(int width, int height)
    {
        int largest = Math.Max(width, height);
        return (int)Math.Floor(Math.Log2(largest)) + 1;
    }

    /// <summary>
    /// Build a BC1 (DXT1) DDS with a full mip chain. Each block is filled by
    /// <paramref name="blockWriter"/>, which receives the block span and the
    /// mip level index.
    /// </summary>
    public static byte[] CreateBc1(int width, int height, int? mipCount,
                                   Action<Span<byte>, int> blockWriter)
    {
        int mips = mipCount ?? FullMipCount(width, height);
        return CreateBlockCompressed(width, height, mips, "DXT1", 8, blockWriter);
    }

    /// <summary>Build a BC3 (DXT5) DDS with a full mip chain.</summary>
    public static byte[] CreateBc3(int width, int height, int? mipCount,
                                   Action<Span<byte>, int> blockWriter)
    {
        int mips = mipCount ?? FullMipCount(width, height);
        return CreateBlockCompressed(width, height, mips, "DXT5", 16, blockWriter);
    }

    private static byte[] CreateBlockCompressed(
        int width, int height, int mips, string fourCc, int blockBytes,
        Action<Span<byte>, int> blockWriter)
    {
        int headerBytes = 128;
        var levels = new List<int>();
        int total = 0;

        for (int level = 0; level < mips; level++)
        {
            int w = Math.Max(1, width >> level);
            int h = Math.Max(1, height >> level);
            int bytes = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * blockBytes;
            levels.Add(bytes);
            total += bytes;
        }

        var data = new byte[headerBytes + total];

        // magic
        data[0] = (byte)'D';
        data[1] = (byte)'D';
        data[2] = (byte)'S';
        data[3] = (byte)' ';
        WriteUInt32(data, 4, 124);                    // dwSize
        WriteUInt32(data, 8, 0x00081007);             // dwFlags: caps|height|width|pitch|pixelformat|linearsize
        WriteUInt32(data, 12, (uint)height);          // dwHeight
        WriteUInt32(data, 16, (uint)width);           // dwWidth
        WriteUInt32(data, 20, (uint)(levels.Count > 0 ? levels[0] : 0)); // dwPitchOrLinearSize
        WriteUInt32(data, 24, 0);                     // dwDepth
        WriteUInt32(data, 28, (uint)mips);            // dwMipMapCount

        WriteUInt32(data, 76, 32);                    // ddspf.dwSize
        WriteUInt32(data, 80, 0x4);                   // ddspf.dwFlags = DDPF_FOURCC
        WriteFourCc(data, 84, fourCc);                // ddspf.dwFourCC

        WriteUInt32(data, 108, 0x00401008);           // dwCaps

        int cursor = headerBytes;
        for (int level = 0; level < mips; level++)
        {
            int bytes = levels[level];
            for (int offset = 0; offset < bytes; offset += blockBytes)
            {
                blockWriter(data.AsSpan(cursor + offset, blockBytes), level);
            }
            cursor += bytes;
        }

        return data;
    }

    /// <summary>
    /// Write a 4-colour-mode BC1 block (c0 &gt; c1) with a repeating index
    /// pattern, so the decoded gradient is non-trivial.
    /// </summary>
    public static void WriteFourColorBlock(Span<byte> block, Rgb c0, Rgb c1, byte indexByte = 0xE4)
    {
        ushort packed0 = c0.ToRgb565Perceptual();
        ushort packed1 = c1.ToRgb565Perceptual();

        // Guarantee the strict ordering this mode requires.
        if (packed0 <= packed1) (packed0, packed1) = (packed1, packed0);

        block[0] = (byte)(packed0 & 0xFF);
        block[1] = (byte)(packed0 >> 8);
        block[2] = (byte)(packed1 & 0xFF);
        block[3] = (byte)(packed1 >> 8);
        block[4] = indexByte;
        block[5] = indexByte;
        block[6] = indexByte;
        block[7] = indexByte;
    }

    /// <summary>
    /// Write a 3-colour-mode BC1 block (c0 &lt;= c1), the mode that carries
    /// 1-bit alpha. Used to prove the mode survives a hue shift.
    /// </summary>
    public static void WriteThreeColorBlock(Span<byte> block, Rgb c0, Rgb c1, byte indexByte = 0xE4)
    {
        ushort packed0 = c0.ToRgb565Perceptual();
        ushort packed1 = c1.ToRgb565Perceptual();

        if (packed0 > packed1) (packed0, packed1) = (packed1, packed0);
        if (packed0 == packed1) packed1++;

        block[0] = (byte)(packed0 & 0xFF);
        block[1] = (byte)(packed0 >> 8);
        block[2] = (byte)(packed1 & 0xFF);
        block[3] = (byte)(packed1 >> 8);
        block[4] = indexByte;
        block[5] = indexByte;
        block[6] = indexByte;
        block[7] = indexByte;
    }

    /// <summary>
    /// Write a BC3 block whose alpha half is a recognisable pattern that must
    /// survive a colour shift untouched.
    /// </summary>
    public static void WriteBc3Block(Span<byte> block, Rgb c0, Rgb c1)
    {
        // Alpha endpoints plus a distinctive 3-bit index pattern.
        block[0] = 0x10;
        block[1] = 0xF0;
        block[2] = 0x1B;
        block[3] = 0x6C;
        block[4] = 0xD8;
        block[5] = 0x42;
        block[6] = 0xA5;
        block[7] = 0x39;

        WriteFourColorBlock(block[8..], c0, c1);
    }

    /// <summary>Read the two packed endpoints of a BC1-style colour block.</summary>
    public static (ushort Packed0, ushort Packed1) ReadPacked(Span<byte> colorBlock) =>
        ((ushort)(colorBlock[0] | (colorBlock[1] << 8)),
         (ushort)(colorBlock[2] | (colorBlock[3] << 8)));

    /// <summary>Read the four index bytes of a BC1-style colour block.</summary>
    public static byte[] ReadIndices(Span<byte> colorBlock) =>
        [colorBlock[4], colorBlock[5], colorBlock[6], colorBlock[7]];

    /// <summary>Decode a BC1-style colour block into 16 pixels.</summary>
    public static Rgb[] DecodeBlock(Span<byte> colorBlock)
    {
        var pixels = new Rgb[16];
        Bc1Block.Decode(colorBlock, pixels);
        return pixels;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteFourCc(byte[] data, int offset, string fourCc)
    {
        for (int i = 0; i < 4; i++)
        {
            data[offset + i] = (byte)(i < fourCc.Length ? fourCc[i] : 0);
        }
    }
}
