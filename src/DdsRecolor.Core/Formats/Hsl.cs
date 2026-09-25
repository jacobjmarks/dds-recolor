namespace DdsRecolor.Core.Formats;

/// <summary>
/// An HSL colour: hue in degrees [0,360), saturation and lightness in [0,1].
/// </summary>
/// <remarks>
/// <para>
/// HSL is supported as an <em>input</em> format because it is what people reach
/// for when describing a colour out loud, and because the CSS <c>hsl()</c> form
/// is widely emitted by design tools.
/// </para>
/// <para>
/// Note that HSL and HSV are different models with confusingly similar names.
/// Both have the same hue. HSL's lightness runs white-to-black with 0.5 being the
/// most colourful point, whereas HSV's value runs black-to-full-colour. The
/// engine itself works in HSV, so HSL input is converted here and never used
/// internally.
/// </para>
/// </remarks>
public readonly record struct Hsl(double H, double S, double L)
{
    /// <summary>Convert an sRGB colour to HSL.</summary>
    public static Hsl FromRgb(Rgb colour)
    {
        double r = colour.R / 255.0;
        double g = colour.G / 255.0;
        double b = colour.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double lightness = (max + min) / 2.0;

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

        // The denominator collapses at pure black and pure white, where
        // saturation is undefined. Reporting zero there is the conventional
        // choice and round-trips correctly.
        double denominator = 1.0 - Math.Abs((2.0 * lightness) - 1.0);
        double saturation = denominator <= double.Epsilon ? 0.0 : delta / denominator;

        return new Hsl(hue, saturation, lightness);
    }

    /// <summary>Convert HSL to sRGB.</summary>
    public Rgb ToRgb()
    {
        double h = H % 360.0;
        if (h < 0.0) h += 360.0;

        double c = (1.0 - Math.Abs((2.0 * L) - 1.0)) * S;
        double x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        double m = L - (c / 2.0);

        (double r, double g, double b) = h switch
        {
            < 60.0 => (c, x, 0.0),
            < 120.0 => (x, c, 0.0),
            < 180.0 => (0.0, c, x),
            < 240.0 => (0.0, x, c),
            < 300.0 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return new Rgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double value)
    {
        double scaled = value * 255.0;
        if (scaled <= 0.0) return 0;
        if (scaled >= 255.0) return 255;
        return (byte)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    public override string ToString() =>
        $"hsl({H:F1} {S * 100:F1}% {L * 100:F1}%)";
}
