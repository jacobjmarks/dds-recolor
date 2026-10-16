namespace DdsRecolor.Core.Io;

using System.Buffers.Binary;
using DdsRecolor.Core.Formats;

/// <summary>RGBA pixels for the first slice of a DDS base mip.</summary>
public sealed record DdsPreviewImage(int Width, int Height, byte[] RgbaPixels);

/// <summary>Decodes the recolorer's BC1/BC2/BC3 formats for image previews.</summary>
public static class DdsPreviewDecoder
{
    public static DdsPreviewImage Decode(DdsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        DdsHeader header = file.Header;
        if (!file.CanRecolorInPlace)
        {
            throw new DdsFormatException(
                $"{header.Format.DisplayName()} cannot be previewed by the block decoder.");
        }

        int width = header.Width;
        int height = header.Height;
        int blocksWide = Math.Max(1, (width + 3) / 4);
        int blocksHigh = Math.Max(1, (height + 3) / 4);
        int levelOffset = header.MipLevels[0].Offset;
        int colorOffset = header.Format.ColorBlockOffset();
        int blockBytes = header.BlockBytes;
        var pixels = new byte[width * height * 4];
        Span<Rgb> colors = stackalloc Rgb[16];
        Span<byte> alpha = stackalloc byte[16];

        for (int blockY = 0; blockY < blocksHigh; blockY++)
        {
            for (int blockX = 0; blockX < blocksWide; blockX++)
            {
                int blockOffset = levelOffset + ((blockY * blocksWide + blockX) * blockBytes);
                ReadOnlySpan<byte> block = file.Data.AsSpan(blockOffset, blockBytes);
                ReadOnlySpan<byte> colorBlock = block.Slice(colorOffset, Bc1Block.Size);
                Bc1Block.Decode(colorBlock, colors);
                DecodeAlpha(header.Format, block, colorBlock, alpha);

                for (int localY = 0; localY < 4; localY++)
                {
                    int y = blockY * 4 + localY;
                    if (y >= height) break;

                    for (int localX = 0; localX < 4; localX++)
                    {
                        int x = blockX * 4 + localX;
                        if (x >= width) break;

                        int localIndex = localY * 4 + localX;
                        int pixelOffset = (y * width + x) * 4;
                        Rgb color = colors[localIndex];
                        pixels[pixelOffset] = color.R;
                        pixels[pixelOffset + 1] = color.G;
                        pixels[pixelOffset + 2] = color.B;
                        pixels[pixelOffset + 3] = alpha[localIndex];
                    }
                }
            }
        }

        return new DdsPreviewImage(width, height, pixels);
    }

    private static void DecodeAlpha(
        DdsFormat format, ReadOnlySpan<byte> block, ReadOnlySpan<byte> colorBlock,
        Span<byte> alpha)
    {
        if (format == DdsFormat.BC1)
        {
            bool fourColor = Bc1Block.IsFourColorMode(colorBlock);
            for (int i = 0; i < 16; i++)
            {
                int index = (colorBlock[4 + (i >> 2)] >> ((i & 3) * 2)) & 0x3;
                alpha[i] = !fourColor && index == 3 ? (byte)0 : (byte)255;
            }
            return;
        }

        if (format == DdsFormat.BC2)
        {
            ulong alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(block[..8]);
            for (int i = 0; i < 16; i++)
            {
                alpha[i] = (byte)(((alphaBits >> (i * 4)) & 0xF) * 17);
            }
            return;
        }

        DecodeBc3Alpha(block[..8], alpha);
    }

    private static void DecodeBc3Alpha(ReadOnlySpan<byte> block, Span<byte> alpha)
    {
        Span<byte> palette = stackalloc byte[8];
        palette[0] = block[0];
        palette[1] = block[1];

        if (palette[0] > palette[1])
        {
            for (int i = 1; i <= 6; i++)
            {
                palette[i + 1] = (byte)(((7 - i) * palette[0] + i * palette[1]) / 7);
            }
        }
        else
        {
            for (int i = 1; i <= 4; i++)
            {
                palette[i + 1] = (byte)(((5 - i) * palette[0] + i * palette[1]) / 5);
            }
            palette[6] = 0;
            palette[7] = 255;
        }

        ulong indices = BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16;
        for (int i = 0; i < 16; i++)
        {
            alpha[i] = palette[(int)((indices >> (i * 3)) & 0x7)];
        }
    }
}