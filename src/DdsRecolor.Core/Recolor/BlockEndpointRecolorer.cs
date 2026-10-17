namespace DdsRecolor.Core.Recolor;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Io;

/// <summary>Result of recolouring one texture.</summary>
public sealed class RecolorResult
{
    /// <summary>The recoloured file bytes.</summary>
    public required byte[] Data { get; init; }

    /// <summary>Number of blocks whose endpoints were rewritten.</summary>
    public int BlocksModified { get; init; }

    /// <summary>Total number of blocks visited.</summary>
    public int BlocksVisited { get; init; }

    /// <summary>
    /// Blocks whose endpoint ordering had to be swapped (and indices inverted)
    /// to preserve the BC1 colour mode.
    /// </summary>
    public int BlocksWithModeInversion { get; init; }

    /// <summary>Blocks whose distinct source endpoints quantized to one RGB565 value.</summary>
    public int BlocksWithEndpointCollision { get; init; }
}

/// <summary>
/// Recolors a texture by rewriting only the RGB565 endpoint words inside each
/// block, leaving the colour index bits and every other byte untouched.
/// </summary>
/// <remarks>
/// <para>
/// This is the default engine because it satisfies the hard requirements
/// structurally rather than approximately:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Exact file size.</b> Only the four endpoint bytes of each block are
/// written. No block changes size, so the output length equals the input length
/// by construction and the mip offset table stays valid.
/// </item>
/// <item>
/// <b>No block-seam artifacts.</b> The 2-bit colour indices are never modified,
/// so the spatial pattern inside every 4x4 block is bit-identical to the
/// original. There is no re-quantisation of the block structure, which is what
/// would otherwise introduce visible tiling on a re-encode.
/// </item>
/// <item>
/// <b>Determinism.</b> The transform is pure arithmetic over the source bytes
/// with no threading, randomness or GPU involvement, so a re-run over the same
/// input produces a byte-identical output.
/// </item>
/// <item>
/// <b>Header preservation.</b> The header is never re-serialised; unknown and
/// vendor-specific fields survive untouched.
/// </item>
/// </list>
/// <para>
/// The trade-off is precision: endpoints are quantised to RGB565, so the
/// achievable colour shift is limited by the 5-6-5 grid. Because the indices
/// still interpolate between the two shifted endpoints, interior pixel error
/// stays well below one 565 step in practice.
/// </para>
/// </remarks>
public sealed class BlockEndpointRecolorer
{
    private readonly HueShift _shift;

    public BlockEndpointRecolorer(HueShift shift) => _shift = shift;

    /// <summary>True when this engine can safely rewrite the given format.</summary>
    public static bool CanHandle(DdsFormat format) => format.IsEndpointHueShiftable();

    /// <summary>Recolor a whole DDS file.</summary>
    public RecolorResult Recolor(byte[] source, DdsHeader header)
    {
        if (!CanHandle(header.Format))
        {
            throw new DdsFormatException(
                $"{header.Format.DisplayName()} is not endpoint-addressable; " +
                "the block engine cannot recolor it.");
        }

        // Work on a copy. Everything outside the four endpoint bytes of each
        // block is copied through verbatim, including the whole header.
        var output = (byte[])source.Clone();

        int blocksModified = 0;
        int blocksVisited = 0;
        int inversions = 0;
        int endpointCollisions = 0;

        int colorOffset = header.Format.ColorBlockOffset();
        int blockBytes = header.BlockBytes;

        // BC2 and BC3 carry a BC1 colour sub-block that is always four-colour;
        // the 1-bit-alpha reading of BC1's 3-colour mode is not defined for
        // them, so their ordering must never be allowed to invert.
        //
        // BC1 is different: the two modes are both valid and the file already
        // records which one each block uses in the c0/c1 ordering. Forcing
        // every BC1 block to four-colour would silently destroy the 1-bit alpha
        // channel on any block that legitimately used 3-colour mode, so the
        // per-block mode is read back and preserved instead.
        bool forceFourColorMode = header.Format is DdsFormat.BC2 or DdsFormat.BC3;

        foreach (var (offset, length) in header.MipLevels)
        {
            if (offset < 0 || offset + length > output.Length)
            {
                throw new DdsFormatException(
                    $"Mip level at {offset} with length {length} falls outside the " +
                    $"{output.Length}-byte file.");
            }

            for (int block = offset; block < offset + length; block += blockBytes)
            {
                blocksVisited++;

                var colorBlock = output.AsSpan(block + colorOffset, Bc1Block.Size);
                var (c0, c1) = Bc1Block.ReadEndpoints(colorBlock);
                var (originalPacked0, originalPacked1) = Bc1Block.ReadPackedEndpoints(colorBlock);

                // Read the mode this block is currently in, before any write.
                bool blockWasFourColor = !forceFourColorMode && Bc1Block.IsFourColorMode(colorBlock);
                bool preserveDistinctEndpoints = originalPacked0 != originalPacked1;

                Rgb newC0 = _shift.Apply(c0);
                Rgb newC1 = _shift.Apply(c1);

                if (newC0 == c0 && newC1 == c1)
                {
                    // Nothing to do for this block. Skipping the write keeps the
                    // output byte-identical to the input wherever the transform
                    // had no effect, which makes the intensity-0 test exact.
                    continue;
                }

                // For BC2/BC3 the required mode is always four-colour. For BC1,
                // preserve whichever mode the block was already in.
                bool requireFourColor = forceFourColorMode || blockWasFourColor;

                if (Bc1Block.WriteEndpoints(
                    colorBlock, newC0, newC1, requireFourColor, preserveDistinctEndpoints,
                    out bool endpointCollision))
                {
                    inversions++;
                }

                if (endpointCollision) endpointCollisions++;
                blocksModified++;
            }
        }

        return new RecolorResult
        {
            Data = output,
            BlocksModified = blocksModified,
            BlocksVisited = blocksVisited,
            BlocksWithModeInversion = inversions,
            BlocksWithEndpointCollision = endpointCollisions,
        };
    }
}
