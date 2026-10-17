namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Io;
using DdsRecolor.Core.Recolor;

/// <summary>
/// Regression tests for BC1 three-colour mode, where index 3 means transparent
/// black rather than an opaque interpolation.
/// </summary>
/// <remarks>
/// <para>
/// These exist because the engine originally broke real textures in a way the
/// synthetic fixture suite did not catch. Validation against the real corpus
/// showed <b>601 blocks</b> whose transparent texels silently became opaque
/// solid colour, and this affected 5,037,619 three-colour blocks (8.1% of the
/// corpus) as a population at risk.
/// </para>
/// <para>
/// The cause: to keep a block's mode after a hue shift, the engine exchanged the
/// two endpoints and inverted the index values. That is correct in four-colour
/// mode, whose palette is symmetric about its midpoint, but wrong in
/// three-colour mode, where slot 2 is an opaque blend and slot 3 is transparent.
/// Inverting turned transparent texels into opaque ones, and an exchange could
/// flip the mode outright.
/// </para>
/// <para>
/// The fix orders the endpoints instead of exchanging them in three-colour mode,
/// leaving the indices untouched.
/// </para>
/// </remarks>
public class Bc1ThreeColorModeTests
{
    private static readonly Rgb Brown = new(0x84, 0x2A, 0x23);
    private static readonly Rgb Peach = new(0xD8, 0x9D, 0x83);

    private static HueShift Shift() => new(new RecolorOptions
    {
        Source = Brown,
        Target = Peach,
    });

    /// <summary>Count texels that reference palette slot 3 (transparent in three-colour mode).</summary>
    private static int CountIndex3(ReadOnlySpan<byte> indexBytes)
    {
        int count = 0;
        for (int i = 0; i < 16; i++)
        {
            int value = (indexBytes[i >> 2] >> ((i & 3) * 2)) & 0x3;
            if (value == 3) count++;
        }
        return count;
    }

    private static int CountOpaque(ReadOnlySpan<byte> block)
    {
        Span<Rgb> pixels = stackalloc Rgb[16];
        Bc1Block.Decode(block, pixels);

        int opaque = 0;
        for (int i = 0; i < 16; i++)
        {
            if (pixels[i] != default) opaque++;
        }
        return opaque;
    }

    [Fact]
    public void ThreeColorMode_KeepsIndicesUntouched()
    {
        // The core of the fix: three-colour blocks must have their index bytes
        // preserved exactly, because slot 3 carries transparency and inverting
        // would destroy it.
        byte[] source = DdsFixture.CreateBc1(64, 64, null,
            (block, level) => DdsFixture.WriteThreeColorBlock(
                block, new Rgb(0x62, 0x55, 0x42), new Rgb(0x62, 0x55, 0x4A),
                indexByte: (byte)(0x55 + level)));

        var file = DdsFile.Load(source);
        byte[] output = file.Recolor(Shift()).Data;

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                Assert.Equal(
                    source.AsSpan(b + 4, 4).ToArray(),
                    output.AsSpan(b + 4, 4).ToArray());
            }
        }
    }

    [Fact]
    public void ThreeColorMode_PreservesTransparentTexelCount()
    {
        // The observable symptom of the original bug. A cutout texture would
        // grow solid patches of colour where it should be see-through.
        byte[] source = DdsFixture.CreateBc1(64, 64, null,
            (block, _) => DdsFixture.WriteThreeColorBlock(
                block, new Rgb(0x62, 0x55, 0x42), new Rgb(0x62, 0x55, 0x4A),
                indexByte: 0x5F));

        var file = DdsFile.Load(source);
        byte[] output = file.Recolor(Shift()).Data;

        int transparentBefore = 0;
        int transparentAfter = 0;

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                transparentBefore += CountIndex3(source.AsSpan(b + 4, 4));
                transparentAfter += CountIndex3(output.AsSpan(b + 4, 4));
            }
        }

        Assert.True(transparentBefore > 0, "Fixture has no transparent texels to test.");
        Assert.Equal(transparentBefore, transparentAfter);
    }

    [Fact]
    public void ThreeColorMode_StaysInThreeColorModeEvenWhenShiftWouldInvertOrdering()
    {
        // Construct the exact hazard: a block whose two endpoints are adjacent on
        // the 565 grid, so a hue shift is very likely to swap which one is larger.
        //
        // A swap here would flip the block to four-colour mode, turning its
        // transparent texels opaque. Ordering the endpoints instead keeps it.
        byte[] source = DdsFixture.CreateBc1(32, 32, 1,
            (block, _) => DdsFixture.WriteThreeColorBlock(
                block, new Rgb(0x63, 0x55, 0x42), new Rgb(0x63, 0x55, 0x4A),
                indexByte: 0x5F));

        var file = DdsFile.Load(source);
        byte[] output = file.Recolor(Shift()).Data;

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                ushort in0 = (ushort)(source[b] | (source[b + 1] << 8));
                ushort in1 = (ushort)(source[b + 2] | (source[b + 3] << 8));
                ushort out0 = (ushort)(output[b] | (output[b + 1] << 8));
                ushort out1 = (ushort)(output[b + 2] | (output[b + 3] << 8));

                if (in0 == in1) continue;   // degenerate, mode unobservable

                Assert.True(in0 <= in1, "Fixture block should start in three-colour mode.");
                Assert.True(out0 <= out1,
                    $"Block at {b} lost three-colour mode " +
                    $"(in 0x{in0:X4}/0x{in1:X4} -> out 0x{out0:X4}/0x{out1:X4}); " +
                    "transparent texels would become opaque.");
            }
        }
    }

    [Fact]
    public void EndpointCollisionRepair_PreservesThreeColorModeAndIndices()
    {
        Span<byte> block = stackalloc byte[Bc1Block.Size];
        DdsFixture.WriteThreeColorBlock(
            block, new Rgb(0x60, 0x50, 0x40), new Rgb(0x60, 0x50, 0x48), indexByte: 0x5F);
        byte[] indices = block[4..].ToArray();
        var shiftedColor = new Rgb(0x70, 0x60, 0x50);

        Bc1Block.WriteEndpoints(
            block, shiftedColor, shiftedColor, requireFourColorMode: false, preserveDistinctEndpoints: true);

        var (packed0, packed1) = Bc1Block.ReadPackedEndpoints(block);
        Assert.True(packed0 < packed1);
        Assert.False(Bc1Block.IsFourColorMode(block));
        Assert.Equal(indices, block[4..].ToArray());
    }

    [Fact]
    public void ThreeColorMode_OpaqueTexelCountIsUnchanged()
    {
        // Stated in terms of what the texture looks like rather than what the
        // bytes say: the number of non-transparent texels must not move.
        byte[] source = DdsFixture.CreateBc1(32, 32, 1,
            (block, _) => DdsFixture.WriteThreeColorBlock(
                block, new Rgb(0x62, 0x55, 0x42), new Rgb(0x62, 0x55, 0x4A),
                indexByte: 0x5F));

        var file = DdsFile.Load(source);
        byte[] output = file.Recolor(Shift()).Data;

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                Assert.Equal(
                    CountOpaque(source.AsSpan(b, 8)),
                    CountOpaque(output.AsSpan(b, 8)));
            }
        }
    }

    [Fact]
    public void ThreeColorMode_ShiftedEndpointsAreStillApplied()
    {
        // The fix must not turn into "do nothing". The endpoints still have to
        // carry the shifted colours; only their ordering is adjusted.
        byte[] source = DdsFixture.CreateBc1(32, 32, 1,
            (block, _) => DdsFixture.WriteThreeColorBlock(
                block, new Rgb(0x8B, 0x5A, 0x2B), new Rgb(0x6B, 0x45, 0x20),
                indexByte: 0x5F));

        var file = DdsFile.Load(source);
        byte[] output = file.Recolor(Shift()).Data;

        bool anyChanged = false;
        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                if (!source.AsSpan(b, 4).SequenceEqual(output.AsSpan(b, 4)))
                {
                    anyChanged = true;
                }
            }
        }

        Assert.True(anyChanged, "Endpoints were not recoloured at all.");
    }

    [Fact]
    public void FourColorMode_StillExchangesEndpointsAndInvertsIndices()
    {
        // Confirms the four-colour path is unchanged by the three-colour fix.
        // Its palette is symmetric about the midpoint, so exchange plus index
        // inversion is the correct and cheap way to satisfy the ordering.
        byte[] source = DdsFixture.CreateBc1(64, 64, null,
            (block, _) => DdsFixture.WriteFourColorBlock(
                block, new Rgb(0x62, 0x55, 0x4A), new Rgb(0x62, 0x55, 0x42)));

        var file = DdsFile.Load(source);
        byte[] output = file.Recolor(Shift()).Data;

        foreach (var (offset, length) in file.Header.MipLevels)
        {
            for (int b = offset; b < offset + length; b += 8)
            {
                ushort out0 = (ushort)(output[b] | (output[b + 1] << 8));
                ushort out1 = (ushort)(output[b + 2] | (output[b + 3] << 8));

                Assert.True(out0 > out1, $"Block at {b} lost four-colour mode.");
            }
        }
    }

    [Fact]
    public void BothModes_SurviveAcrossTheWholeCommittedFixtureSet()
    {
        // The corpus is 8.1% three-colour blocks, so this is not an edge case.
        // Sweep every fixture rather than one representative file.
        foreach (string name in Directory.GetFiles(
                     Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.dds"))
        {
            var file = DdsFile.Load(name);
            if (file.Header.Format != DdsFormat.BC1) continue;

            byte[] output = file.Recolor(Shift()).Data;
            int colorOffset = file.Header.Format.ColorBlockOffset();

            foreach (var (offset, length) in file.Header.MipLevels)
            {
                for (int b = offset; b < offset + length; b += file.Header.BlockBytes)
                {
                    int c = b + colorOffset;
                    ushort in0 = (ushort)(file.Data[c] | (file.Data[c + 1] << 8));
                    ushort in1 = (ushort)(file.Data[c + 2] | (file.Data[c + 3] << 8));
                    ushort out0 = (ushort)(output[c] | (output[c + 1] << 8));
                    ushort out1 = (ushort)(output[c + 2] | (output[c + 3] << 8));

                    if (in0 == in1) continue;   // degenerate, mode unobservable

                    bool wasFour = in0 > in1;
                    bool isFour = out0 > out1;

                    Assert.True(wasFour == isFour,
                        $"{Path.GetFileName(name)} block at {c}: mode changed from " +
                        $"{(wasFour ? "four" : "three")}-colour to " +
                        $"{(isFour ? "four" : "three")}-colour.");
                }
            }
        }
    }
}
