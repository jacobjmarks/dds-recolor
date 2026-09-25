namespace DdsRecolor.Core.Formats;

/// <summary>
/// An 8-bit-per-channel RGB colour, plus the conversions to and from the RGB565
/// packed endpoints that BC1/BC2/BC3 blocks use.
/// </summary>
/// <remarks>
/// All intermediate maths is done at 8-bit (or higher) precision and quantised
/// to 565 exactly once, at the end. Quantising once, with rounding rather than
/// truncation, keeps the error around half a 565 step instead of compounding it
/// across unpack/repack cycles.
/// </remarks>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>Widen a 5-bit channel to 8 bits by replicating its high bits.</summary>
    public static byte Expand5(int value5) => (byte)((value5 << 3) | (value5 >> 2));

    /// <summary>Widen a 6-bit channel to 8 bits by replicating its high bits.</summary>
    public static byte Expand6(int value6) => (byte)((value6 << 2) | (value6 >> 4));

    /// <summary>Narrow an 8-bit channel to 5 bits, rounding to nearest.</summary>
    public static int Narrow5(byte value8) => (value8 * 31 + 127) / 255;

    /// <summary>Narrow an 8-bit channel to 6 bits, rounding to nearest.</summary>
    public static int Narrow6(byte value8) => (value8 * 63 + 127) / 255;

    /// <summary>Unpack an RGB565 endpoint into 8-bit channels.</summary>
    /// <remarks>
    /// Layout, little-endian uint16: bits 15..11 red, 10..5 green, 4..0 blue.
    /// </remarks>
    public static Rgb FromRgb565(ushort packed) => new(
        Expand5((packed >> 11) & 0x1F),
        Expand6((packed >> 5) & 0x3F),
        Expand5(packed & 0x1F));

    /// <summary>Pack 8-bit channels into an RGB565 endpoint.</summary>
    public static ushort ToRgb565(Rgb colour) => (ushort)(
        (Narrow5(colour.R) << 11) |
        (Narrow6(colour.G) << 5) |
        Narrow5(colour.B));

    /// <summary>Pack this colour into an RGB565 endpoint.</summary>
    public ushort ToRgb565() => ToRgb565(this);

    /// <summary>
    /// Squared Euclidean distance in RGB space. Used only to compare candidate
    /// quantisations against each other, so the absolute scale is irrelevant.
    /// </summary>
    public static int DistanceSquared(Rgb a, Rgb b)
    {
        int dr = a.R - b.R;
        int dg = a.G - b.G;
        int db = a.B - b.B;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    /// <summary>
    /// Quantise to 565 and choose the packed value whose decoded colour is
    /// closest to the intended one.
    /// </summary>
    /// <remarks>
    /// Plain round-to-nearest per channel is usually optimal, but the two 5-bit
    /// channels have a coarser step than the 6-bit green channel, so rounding
    /// each channel independently can occasionally land on a pack whose decoded
    /// colour is measurably further away than a neighbour. Checking the 27
    /// candidates in the surrounding 3x3x3 cell removes that class of error for
    /// a trivial cost, which matters because every visible block endpoint goes
    /// through here.
    /// </remarks>
    public ushort ToRgb565Perceptual()
    {
        int r5 = Narrow5(R);
        int g6 = Narrow6(G);
        int b5 = Narrow5(B);

        ushort best = 0;
        int bestDistance = int.MaxValue;

        for (int dr = -1; dr <= 1; dr++)
        {
            int cr = r5 + dr;
            if (cr < 0 || cr > 31) continue;

            for (int dg = -1; dg <= 1; dg++)
            {
                int cg = g6 + dg;
                if (cg < 0 || cg > 63) continue;

                for (int db = -1; db <= 1; db++)
                {
                    int cb = b5 + db;
                    if (cb < 0 || cb > 31) continue;

                    var candidate = (ushort)((cr << 11) | (cg << 5) | cb);
                    int distance = DistanceSquared(FromRgb565(candidate), this);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Linear interpolation, used to reconstruct BC1's 2/3 and 1/3 points.</summary>
    public static Rgb Lerp(Rgb a, Rgb b, int numerator, int denominator)
    {
        return new Rgb(
            (byte)(((a.R * (denominator - numerator)) + (b.R * numerator)) / denominator),
            (byte)(((a.G * (denominator - numerator)) + (b.G * numerator)) / denominator),
            (byte)(((a.B * (denominator - numerator)) + (b.B * numerator)) / denominator));
    }

    /// <summary>Midpoint, used by BC1's 3-colour mode and BC3's alpha block.</summary>
    public static Rgb Average(Rgb a, Rgb b) => Lerp(a, b, 1, 2);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}
