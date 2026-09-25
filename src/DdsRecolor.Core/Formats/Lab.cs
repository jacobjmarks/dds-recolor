namespace DdsRecolor.Core.Formats;

/// <summary>
/// A CIELAB (L*a*b*) colour with a D65 white point, as used by CSS.
/// </summary>
/// <param name="L">Lightness, 0 (black) to 100 (white).</param>
/// <param name="A">Green to red axis, roughly -125 to 125 in practice.</param>
/// <param name="B">Blue to yellow axis, roughly -125 to 125 in practice.</param>
/// <remarks>
/// <para>
/// CIELAB is perceptually uniform, which is why it is worth supporting as an
/// <em>input</em> format even though the engine works in HSV: picking a target
/// by eye in Lab is closer to "make it this much lighter" than in RGB, where
/// brightness changes are non-linear.
/// </para>
/// <para>
/// The conversion path is sRGB (gamma) to linear RGB, to XYZ with a D65 white
/// point, then to Lab. CSS's <c>lab()</c> uses D50 by default, but D65 is what
/// an sRGB display and this tool's RGB path use, so D65 is used throughout to
/// keep a round trip lossless. The difference is a fraction of a unit for
/// typical colours.
/// </para>
/// </remarks>
public readonly record struct Lab(double L, double A, double B)
{
    // D65 white point, the reference for sRGB.
    private const double WhiteX = 95.0470;
    private const double WhiteY = 100.0000;
    private const double WhiteZ = 108.8830;

    // CIE standard constants.
    private const double Epsilon = 216.0 / 24389.0;   // 0.008856...
    private const double Kappa = 24389.0 / 27.0;      // 903.3...

    /// <summary>Convert an sRGB colour to Lab.</summary>
    public static Lab FromRgb(Rgb colour)
    {
        // sRGB companding: undo the display gamma to get linear light.
        static double ToLinear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.04045
                ? c / 12.92
                : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        double r = ToLinear(colour.R);
        double g = ToLinear(colour.G);
        double b = ToLinear(colour.B);

        // Linear sRGB to XYZ, then scale to the 0..100 range Lab expects.
        double x = ((r * 0.4124564) + (g * 0.3575761) + (b * 0.1804375)) * 100.0;
        double y = ((r * 0.2126729) + (g * 0.7151522) + (b * 0.0721750)) * 100.0;
        double z = ((r * 0.0193339) + (g * 0.1191920) + (b * 0.9503041)) * 100.0;

        double fx = PivotForward(x / WhiteX);
        double fy = PivotForward(y / WhiteY);
        double fz = PivotForward(z / WhiteZ);

        return new Lab(
            (116.0 * fy) - 16.0,
            500.0 * (fx - fy),
            200.0 * (fy - fz));
    }

    /// <summary>
    /// Convert Lab back to sRGB, clamping the result into the displayable gamut.
    /// </summary>
    /// <remarks>
    /// Lab can express colours sRGB cannot show. Those clamp rather than wrap,
    /// so an out-of-gamut request produces the nearest displayable colour
    /// instead of an unrelated one.
    /// </remarks>
    public Rgb ToRgb()
    {
        double fy = (L + 16.0) / 116.0;
        double fx = fy + (A / 500.0);
        double fz = fy - (B / 200.0);

        double x = WhiteX * PivotInverse(fx);
        double y = L > (Kappa * Epsilon)
            ? WhiteY * Math.Pow((L + 16.0) / 116.0, 3.0)
            : WhiteY * (L / Kappa);
        double z = WhiteZ * PivotInverse(fz);

        // XYZ back to linear sRGB.
        double xn = x / 100.0;
        double yn = y / 100.0;
        double zn = z / 100.0;

        double r = (xn * 3.2404542) + (yn * -1.5371385) + (zn * -0.4985314);
        double g = (xn * -0.9692660) + (yn * 1.8760108) + (zn * 0.0415560);
        double bl = (xn * 0.0556434) + (yn * -0.2040259) + (zn * 1.0572252);

        return new Rgb(ToSrgbByte(r), ToSrgbByte(g), ToSrgbByte(bl));
    }

    private static double PivotForward(double t) =>
        t > Epsilon ? Math.Cbrt(t) : ((Kappa * t) + 16.0) / 116.0;

    private static double PivotInverse(double t)
    {
        double cubed = t * t * t;
        return cubed > Epsilon ? cubed : ((116.0 * t) - 16.0) / Kappa;
    }

    /// <summary>Apply sRGB companding and quantise, clamping to the displayable range.</summary>
    private static byte ToSrgbByte(double linear)
    {
        double c = linear <= 0.0031308
            ? 12.92 * linear
            : (1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055;

        double scaled = c * 255.0;
        if (scaled <= 0.0) return 0;
        if (scaled >= 255.0) return 255;
        return (byte)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    public override string ToString() => $"lab({L:F1} {A:F1} {B:F1})";
}
