namespace DdsRecolor.Core.Formats;

/// <summary>
/// Builds a colour from the components of each functional syntax, applying the
/// range conventions described on <see cref="ColorParser"/>.
/// </summary>
public static partial class ColorParser
{
    /// <summary>
    /// <c>rgb()</c>, accepting either 0-255 or 0-1 channels plus percentages.
    /// </summary>
    /// <remarks>
    /// The range is inferred per call rather than per component: if any channel
    /// exceeds 1, all three are read as 0-255. Deciding per component would let
    /// <c>rgb(1, 128, 0)</c> mix conventions, which is never what the author
    /// meant and is far harder to spot than an inconsistency.
    /// </remarks>
    private static bool TryBuildRgb(string[] parts, out ParsedColor parsed)
    {
        parsed = default;

        var values = new double[3];
        var isPercent = new bool[3];
        bool anyOverOne = false;

        for (int i = 0; i < 3; i++)
        {
            isPercent[i] = parts[i].EndsWith('%');
            string text = isPercent[i] ? parts[i][..^1] : parts[i];
            if (!TryParseNumber(text, out values[i])) return false;
            if (!isPercent[i] && values[i] > 1.0) anyOverOne = true;
        }

        // A fractional value above 1 is a trap. "rgb(1.5 0 0)" is far more
        // likely to be a mistyped 0-1 value than a deliberate request for
        // channel 2 of 255, and the two readings differ enormously (near-black
        // versus bright red). Refuse it rather than pick one silently.
        //
        // Integers above 1 are unambiguous, so the 0-255 form still works.
        if (anyOverOne)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!isPercent[i] && values[i] > 1.0 && values[i] != Math.Floor(values[i]))
                {
                    return false;
                }
            }
        }

        var channels = new byte[3];
        for (int i = 0; i < 3; i++)
        {
            double value = values[i];

            if (isPercent[i])
            {
                if (value < 0.0 || value > 100.0) return false;
                channels[i] = (byte)Math.Round(value * 255.0 / 100.0, MidpointRounding.AwayFromZero);
            }
            else if (anyOverOne)
            {
                if (value < 0.0 || value > 255.0) return false;
                channels[i] = (byte)Math.Round(value, MidpointRounding.AwayFromZero);
            }
            else
            {
                if (value < 0.0 || value > 1.0) return false;
                channels[i] = (byte)Math.Round(value * 255.0, MidpointRounding.AwayFromZero);
            }
        }

        parsed = new ParsedColor(new Rgb(channels[0], channels[1], channels[2]), ColorFormat.Rgb);
        return true;
    }

    private static bool TryBuildHsl(string[] parts, out ParsedColor parsed)
    {
        parsed = default;
        if (!TryParseHue(parts[0], out double hue)) return false;
        if (!TryParsePercentOrUnit(parts[1], out double saturation)) return false;
        if (!TryParsePercentOrUnit(parts[2], out double lightness)) return false;

        parsed = new ParsedColor(new Hsl(hue, saturation, lightness).ToRgb(), ColorFormat.Hsl);
        return true;
    }

    private static bool TryBuildHsv(string[] parts, out ParsedColor parsed)
    {
        parsed = default;
        if (!TryParseHue(parts[0], out double hue)) return false;
        if (!TryParsePercentOrUnit(parts[1], out double saturation)) return false;
        if (!TryParsePercentOrUnit(parts[2], out double value)) return false;

        parsed = new ParsedColor(new Hsv(hue, saturation, value).ToRgb(), ColorFormat.Hsv);
        return true;
    }

    private static bool TryBuildHwb(string[] parts, out ParsedColor parsed)
    {
        parsed = default;
        if (!TryParseHue(parts[0], out double hue)) return false;
        if (!TryParsePercentOrUnit(parts[1], out double whiteness)) return false;
        if (!TryParsePercentOrUnit(parts[2], out double blackness)) return false;

        parsed = new ParsedColor(new Hwb(hue, whiteness, blackness).ToRgb(), ColorFormat.Hwb);
        return true;
    }

    private static bool TryBuildLab(string[] parts, out ParsedColor parsed)
    {
        parsed = default;

        // Lightness accepts a percentage (CSS) or a bare 0-100 number.
        double lightness;
        if (parts[0].EndsWith('%'))
        {
            if (!TryParseNumber(parts[0][..^1], out double percent)) return false;
            if (percent < 0.0 || percent > 100.0) return false;
            lightness = percent;
        }
        else
        {
            if (!TryParseNumber(parts[0], out lightness)) return false;
            if (lightness < 0.0 || lightness > 100.0) return false;
        }

        // The a and b axes are unbounded mathematically but physically sensible
        // only within roughly +/-160. Reject beyond that so a mistyped value is
        // reported rather than clamped into an arbitrary colour.
        if (!TryParseNumber(parts[1], out double a)) return false;
        if (!TryParseNumber(parts[2], out double b)) return false;
        if (Math.Abs(a) > 200.0 || Math.Abs(b) > 200.0) return false;

        parsed = new ParsedColor(new Lab(lightness, a, b).ToRgb(), ColorFormat.Lab);
        return true;
    }
}
