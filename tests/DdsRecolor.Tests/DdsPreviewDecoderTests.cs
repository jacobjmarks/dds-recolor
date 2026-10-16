namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Io;

public sealed class DdsPreviewDecoderTests
{
    [Fact]
    public void Bc1ThreeColorMode_DecodesTransparentTexels()
    {
        byte[] data = DdsFixture.CreateBc1(4, 4, 1,
            (block, _) => DdsFixture.WriteThreeColorBlock(
                block, new Rgb(32, 64, 96), new Rgb(64, 96, 128), indexByte: 0xFF));

        DdsPreviewImage image = DdsPreviewDecoder.Decode(DdsFile.Load(data));

        Assert.All(Enumerable.Range(0, 16), index => Assert.Equal(0, image.RgbaPixels[index * 4 + 3]));
    }

    [Fact]
    public void Bc2_DecodesExplicitAlphaAndClipsPartialBlocks()
    {
        byte[] data = DdsFixture.CreateBc3(3, 2, 1, (block, _) =>
        {
            block[..8].Fill(0x11);
            DdsFixture.WriteFourColorBlock(block[8..], new Rgb(220, 20, 10), new Rgb(10, 20, 220));
        });
        "DXT3"u8.CopyTo(data.AsSpan(84, 4));

        DdsPreviewImage image = DdsPreviewDecoder.Decode(DdsFile.Load(data));

        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(3 * 2 * 4, image.RgbaPixels.Length);
        Assert.All(Enumerable.Range(0, 6), index => Assert.Equal(17, image.RgbaPixels[index * 4 + 3]));
    }

    [Fact]
    public void Bc3_DecodesAlphaPaletteEntries()
    {
        byte[] data = DdsFixture.CreateBc3(4, 4, 1, (block, _) =>
        {
            block[0] = 0;
            block[1] = 255;
            block[2] = 0b111_110;
            block[3..8].Clear();
            DdsFixture.WriteFourColorBlock(block[8..], new Rgb(220, 20, 10), new Rgb(10, 20, 220));
        });

        DdsPreviewImage image = DdsPreviewDecoder.Decode(DdsFile.Load(data));

        Assert.Equal(0, image.RgbaPixels[3]);
        Assert.Equal(255, image.RgbaPixels[7]);
    }

    [Fact]
    public void UnsupportedFormat_ThrowsClearPreviewError()
    {
        byte[] data = DdsFixture.CreateBc3(4, 4, 1,
            (block, _) => DdsFixture.WriteBc3Block(block, new Rgb(220, 20, 10), new Rgb(10, 20, 220)));
        "ATI2"u8.CopyTo(data.AsSpan(84, 4));

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => DdsPreviewDecoder.Decode(DdsFile.Load(data)));

        Assert.Contains("cannot be previewed", exception.Message);
    }
}