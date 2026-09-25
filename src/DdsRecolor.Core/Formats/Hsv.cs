namespace DdsRecolor.Core.Formats;

/// <summary>
/// An HSV colour with hue in degrees [0,360) and saturation/value in [0,1].
/// </summary>
/// <remarks>
/// HSV is used rather than HSL because rotating hue in HSV leaves value, and
/// therefore perceived shading, exactly untouched. That is the property the
/// recolor relies on to preserve lighting detail and geometry in texture
/// stretches.
/// </remarks>
public readonly record struct Hsv(double H, double S, double V)
{
    public static Hsv FromRgb(Rgb colour)
    {
        double r = colour.R / 255.0;
        double g = colour.G / 255.0;
        double b = colour.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue;
        if (delta <= double.Epsilon)
        {
            hue = 0.0;
        }
        else if (max == r)
        {
            hue = 60.0 * (((g - b) / delta) % 6.0);
        }
        else if (max == g)
        {
            hue = 60.0 * (((b - r) / delta) + 2.0);
        }
        else
        {
            hue = 60.0 * (((r - g) / delta) + 4.0);
        }

        if (hue < 0.0) hue += 360.0;

        double saturation = max <= double.Epsilon ? 0.0 : delta / max;
        return new Hsv(hue, saturation, max);
    }

    public Rgb ToRgb()
    {
        double h = H % 360.0;
        if (h < 0.0) h += 360.0;

        double c = V * S;
        double x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        double m = V - c;

        (double r, double g, double b) = h switch
        {
            < 60.0 => (c, x, 0.0),
            < 120.0 => (x, c, 0.0),
            < 180.0 => (0.0, c, x),
            < 240.0 => (0.0, x, c),
            < 300.0 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new Rgb(
            ToByte(r + m),
            ToByte(g + m),
            ToByte(b + m));
    }

    private static byte ToByte(double value)
    {
        double scaled = value * 255.0;
        if (scaled <= 0.0) return 0;
        if (scaled >= 255.0) return 255;
        return (byte)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Rotate the hue by <paramref name="deltaDegrees"/>, preserving saturation
    /// and value.
    /// </summary>
    public Hsv RotateHue(double deltaDegrees)
    {
        double h = (H + deltaDegrees) % 360.0;
        if (h < 0.0) h += 360.0;
        return this with { H = h };
    }

    /// <summary>
    /// Shortest signed angular distance from <paramref name="from"/> to
    /// <paramref name="to"/>, in degrees, within (-180, 180].
    /// </summary>
    public static double HueDelta(double from, double to)
    {
        double delta = (to - from) % 360.0;
        if (delta > 180.0) delta -= 360.0;
        if (delta <= -180.0) delta += 360.0;
        return delta;
    }
}
