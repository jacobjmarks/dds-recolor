namespace DdsRecolor.Core.Formats;

/// <summary>
/// Read/write access to the colour half of a BC1 (DXT1) block.
/// </summary>
/// <remarks>
/// <para>BC1 block layout, 8 bytes:</para>
/// <code>
///   +0  u16 c0        RGB565 colour endpoint 0
///   +2  u16 c1        RGB565 colour endpoint 1
///   +4  4 bytes       16 x 2-bit colour indices, row-major
/// </code>
/// <para>
/// The comparison between the two endpoints selects the block's mode, and the
/// mode changes what the indices mean:
/// </para>
/// <code>
///   c0 >  c1   4-colour mode: index 2 = 2/3 c0 + 1/3 c1, index 3 = 1/3 c0 + 2/3 c1
///   c0 &lt;= c1   3-colour mode: index 2 = 1/2(c0 + c1), index 3 = transparent black
/// </code>
/// <para>
/// This is the one real correctness hazard in the whole recolor: a hue shift
/// can change which endpoint is numerically larger, silently converting a
/// 4-colour block into a 3-colour-plus-transparent one. <see cref="WriteEndpoints"/>
/// detects and repairs that by swapping the endpoints and inverting the index
/// values, which leaves the decoded image identical while keeping the mode.
/// </para>
/// </remarks>
public static class Bc1Block
{
    /// <summary>Total block size in bytes.</summary>
    public const int Size = 8;

    /// <summary>Read the two colour endpoints from a block.</summary>
    public static (Rgb C0, Rgb C1) ReadEndpoints(ReadOnlySpan<byte> block)
    {
        ushort packed0 = (ushort)(block[0] | (block[1] << 8));
        ushort packed1 = (ushort)(block[2] | (block[3] << 8));
        return (Rgb.FromRgb565(packed0), Rgb.FromRgb565(packed1));
    }

    /// <summary>Read the raw packed endpoints, for mode determination.</summary>
    public static (ushort Packed0, ushort Packed1) ReadPackedEndpoints(ReadOnlySpan<byte> block)
    {
        ushort packed0 = (ushort)(block[0] | (block[1] << 8));
        ushort packed1 = (ushort)(block[2] | (block[3] << 8));
        return (packed0, packed1);
    }

    /// <summary>True when the block is in 4-colour mode (c0 &gt; c1).</summary>
    public static bool IsFourColorMode(ReadOnlySpan<byte> block)
    {
        var (packed0, packed1) = ReadPackedEndpoints(block);
        return packed0 > packed1;
    }

    /// <summary>Write new colour endpoints into a block, preserving its mode.</summary>
    /// <param name="block">The 8-byte BC1-style colour block to rewrite.</param>
    /// <param name="color0">The new colour for the texels that reference slot 0.</param>
    /// <param name="color1">The new colour for the texels that reference slot 1.</param>
    /// <param name="requireFourColorMode">
    /// True to force the block into 4-colour mode (c0 &gt; c1), false to force
    /// 3-colour mode (c0 &lt;= c1). Callers determine the correct value from the
    /// format (BC2/BC3 are always 4-colour) or by reading the block's existing
    /// mode (BC1).
    /// </param>
    /// <param name="preserveDistinctEndpoints">
    /// When quantisation makes the new endpoints equal, move one endpoint to
    /// the nearest legal adjacent RGB565 value to retain encoded contrast.
    /// </param>
    /// <returns>
    /// True when the two endpoint slots had to be exchanged, which requires the
    /// index values to be remapped. Callers surface this as a diagnostic.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The two BC1 modes differ in more than numeric ordering, and the
    /// difference decides how this method may fix up a block:
    /// </para>
    /// <code>
    ///   four-colour (c0 &gt; c1):  0=c0  1=c1  2=(2c0+c1)/3  3=(c0+2c1)/3   all OPAQUE
    ///   three-colour (c0 &lt;= c1): 0=c0  1=c1  2=(c0+c1)/2     3=TRANSPARENT BLACK
    /// </code>
    /// <para>
    /// In <b>four-colour</b> mode the palette is symmetric about its midpoint: if
    /// the endpoints are exchanged, palette slot 2 becomes the old slot 3 and
    /// vice versa. Inverting every 2-bit index (0&lt;-&gt;1, 2&lt;-&gt;3) therefore
    /// leaves the decoded image identical, so exchanging the endpoints is a safe
    /// way to satisfy the ordering requirement.
    /// </para>
    /// <para>
    /// In <b>three-colour</b> mode that reasoning fails, because slot 3 is
    /// transparent black and slot 2 is an opaque blend. They are not symmetric,
    /// so inverting indices would replace transparent texels with opaque ones,
    /// punching holes of solid colour into a cutout. Worse, an exchange can flip
    /// the block's mode outright, since the mode is determined by the ordering of
    /// the two values.
    /// </para>
    /// <para>
    /// So three-colour mode does not exchange the endpoints at all. It
    /// <em>orders</em> them: whichever shifted colour is smaller becomes c0 and
    /// the larger becomes c1. The indices are left untouched, slot 2 stays the
    /// opaque average, slot 3 stays transparent, and the mode is guaranteed
    /// preserved. Texels still read the same slot as before, so the colour
    /// mapping is unaffected; only which slot holds which shifted colour changes,
    /// and both slots carry correctly shifted colours either way.
    /// </para>
    /// </remarks>
    public static bool WriteEndpoints(Span<byte> block, Rgb color0, Rgb color1,
                                      bool requireFourColorMode, bool preserveDistinctEndpoints = false)
        => WriteEndpoints(block, color0, color1, requireFourColorMode,
            preserveDistinctEndpoints, out _);

    internal static bool WriteEndpoints(Span<byte> block, Rgb color0, Rgb color1,
                                        bool requireFourColorMode, bool preserveDistinctEndpoints,
                                        out bool endpointCollision)
    {
        ushort packed0 = color0.ToRgb565Perceptual();
        ushort packed1 = color1.ToRgb565Perceptual();
        endpointCollision = preserveDistinctEndpoints && packed0 == packed1;

        if (endpointCollision)
        {
            (packed0, packed1) = ClosestDistinctPair(
                packed0, color0, color1, requireFourColorMode);
        }

        if (!requireFourColorMode)
        {
            // Three-colour mode: sort the shifted endpoints into c0 <= c1 and
            // leave the indices alone.
            if (packed0 > packed1)
            {
                (packed0, packed1) = (packed1, packed0);
            }

            WritePacked(block, packed0, packed1);
            return false;
        }

        // Four-colour mode needs c0 > c1 strictly.
        //
        // Equal endpoints are degenerate, so accept them unless the caller asked
        // to preserve shading from a formerly distinct pair.
        bool needsExchange = packed0 != packed1 && packed0 <= packed1;

        if (needsExchange)
        {
            (packed0, packed1) = (packed1, packed0);

            // Safe here only because the four-colour palette is symmetric.
            InvertIndices(block);
        }

        WritePacked(block, packed0, packed1);
        return needsExchange;
    }

    private static (ushort Packed0, ushort Packed1) ClosestDistinctPair(
        ushort common, Rgb color0, Rgb color1, bool requireFourColorMode)
    {
        int red = (common >> 11) & 0x1F;
        int green = (common >> 5) & 0x3F;
        int blue = common & 0x1F;
        Span<ushort> neighbors = stackalloc ushort[6];
        int count = 0;

        if (red > 0) neighbors[count++] = (ushort)(common - 0x800);
        if (red < 31) neighbors[count++] = (ushort)(common + 0x800);
        if (green > 0) neighbors[count++] = (ushort)(common - 0x20);
        if (green < 63) neighbors[count++] = (ushort)(common + 0x20);
        if (blue > 0) neighbors[count++] = (ushort)(common - 1);
        if (blue < 31) neighbors[count++] = (ushort)(common + 1);

        ushort best0 = common;
        ushort best1 = common;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            ushort neighbor = neighbors[i];
            Consider(neighbor, common);
            Consider(common, neighbor);
        }

        return (best0, best1);

        void Consider(ushort candidate0, ushort candidate1)
        {
            bool validMode = requireFourColorMode
                ? candidate0 > candidate1
                : candidate0 < candidate1;
            if (!validMode) return;

            int distance = Rgb.DistanceSquared(Rgb.FromRgb565(candidate0), color0)
                + Rgb.DistanceSquared(Rgb.FromRgb565(candidate1), color1);
            if (distance >= bestDistance) return;

            bestDistance = distance;
            best0 = candidate0;
            best1 = candidate1;
        }
    }

    private static void WritePacked(Span<byte> block, ushort packed0, ushort packed1)
    {
        block[0] = (byte)(packed0 & 0xFF);
        block[1] = (byte)(packed0 >> 8);
        block[2] = (byte)(packed1 & 0xFF);
        block[3] = (byte)(packed1 >> 8);
    }

    /// <summary>
    /// Remap every 2-bit index so the decoded image is unchanged after the two
    /// endpoints have been exchanged.
    /// </summary>
    /// <remarks>
    /// 0 and 1 select the two endpoints, 2 and 3 select the two derived colours.
    /// Swapping the endpoints therefore means swapping 0 with 1 and 2 with 3.
    /// This is done with bit tricks so the 16 indices are fixup'd in a handful
    /// of operations rather than a 16-iteration loop.
    /// </remarks>
    private static void InvertIndices(Span<byte> block)
    {
        // Gather the 16 2-bit indices out of the four index bytes.
        uint indices =
            block[4] |
            ((uint)block[5] << 8) |
            ((uint)block[6] << 16) |
            ((uint)block[7] << 24);

        // Swap the low bit of every 2-bit pair: 0 <-> 1 and 2 <-> 3.
        indices ^= 0x55555555u;

        block[4] = (byte)(indices & 0xFF);
        block[5] = (byte)((indices >> 8) & 0xFF);
        block[6] = (byte)((indices >> 16) & 0xFF);
        block[7] = (byte)((indices >> 24) & 0xFF);
    }

    /// <summary>
    /// Decode the 16 pixels of a BC1 block into <paramref name="destination"/>,
    /// which must hold at least 16 entries. Pixels are row-major.
    /// </summary>
    /// <remarks>
    /// Used for verification and preview rendering, not on the rewrite hot path.
    /// Transparent pixels (3-colour mode, index 3) decode to
    /// <see cref="Rgb"/> black, matching hardware behaviour of returning black
    /// for the transparent texel's colour channel.
    /// </remarks>
    public static void Decode(ReadOnlySpan<byte> block, Span<Rgb> destination)
    {
        var (c0, c1) = ReadEndpoints(block);
        var (packed0, packed1) = ReadPackedEndpoints(block);
        bool fourColor = packed0 > packed1;

        Span<Rgb> palette = stackalloc Rgb[4];
        palette[0] = c0;
        palette[1] = c1;
        if (fourColor)
        {
            palette[2] = Rgb.Lerp(c0, c1, 1, 3);
            palette[3] = Rgb.Lerp(c0, c1, 2, 3);
        }
        else
        {
            palette[2] = Rgb.Average(c0, c1);
            palette[3] = default; // transparent black
        }

        for (int i = 0; i < 16; i++)
        {
            int byteIndex = 4 + (i >> 2);
            int shift = (i & 3) * 2;
            int index = (block[byteIndex] >> shift) & 0x3;
            destination[i] = palette[index];
        }
    }
}
