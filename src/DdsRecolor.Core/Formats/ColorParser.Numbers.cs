namespace DdsRecolor.Core.Formats;

/// <summary>
/// Numeric component parsing: hue units, percentages and plain numbers.
/// </summary>
public static partial class ColorParser
{
    /// <summary>
    /// Parse a hue, accepting a bare number as degrees plus the CSS units
    /// <c>deg</c>, <c>grad</c>, <c>rad</c> and <c>turn</c>.
    /// </summary>
    /// <remarks>
    /// Hue wraps rather than erroring, matching CSS: <c>400deg</c> is
    /// <c>40deg</c> and <c>-30deg</c> is <c>330deg</c>. Wrapping is safe because
    /// hue is genuinely cyclic, unlike a saturation of 150% which would be
    /// meaningless.
    /// </remarks>
    private static bool TryParseHue(string text, out double degrees)
    {
        degrees = 0;

        if (text.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseNumber(text[..^3], out degrees)) return false;
        }
        else if (text.EndsWith("grad", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseNumber(text[..^4], out double gradians)) return false;
            degrees = gradians * 360.0 / 400.0;
        }
        else if (text.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseNumber(text[..^3], out double radians)) return false;
            degrees = radians * 180.0 / Math.PI;
        }
        else if (text.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseNumber(text[..^4], out double turns)) return false;
            degrees = turns * 360.0;
        }
        else
        {
            if (!TryParseNumber(text, out degrees)) return false;
        }

        degrees %= 360.0;
        if (degrees < 0.0) degrees += 360.0;
        return true;
    }

    /// <summary>
    /// Parse a component given as a percentage or as a 0-1 fraction.
    /// </summary>
    /// <remarks>
    /// Both spellings are accepted because CSS allows either and different tools
    /// emit different ones. A bare value above 1 is <b>rejected</b> rather than
    /// read as a percentage, so <c>hsl(29 53 36)</c> does not silently mean 53%
    /// and 36% — a mistake that would produce a plausible but wrong colour.
    /// </remarks>
    private static bool TryParsePercentOrUnit(string text, out double result)
    {
        result = 0;

        if (text.EndsWith('%'))
        {
            if (!TryParseNumber(text[..^1], out double percent)) return false;
            if (percent < 0.0 || percent > 100.0) return false;
            result = percent / 100.0;
            return true;
        }

        if (!TryParseNumber(text, out result)) return false;
        return result is >= 0.0 and <= 1.0;
    }

    /// <summary>
    /// Parse a number, rejecting NaN, infinity and anything a
    /// culture-sensitive parse would accept but the colour syntax should not,
    /// such as thousands separators.
    /// </summary>
    private static bool TryParseNumber(string text, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text)) return false;

        // Allow digits, a decimal point, an explicit sign and exponent notation.
        // Anything else, including a group separator or a stray unit letter, is
        // rejected here rather than left for double.TryParse to interpret.
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c) && c is not ('.' or '+' or '-' or 'e' or 'E'))
            {
                return false;
            }
        }

        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return double.IsFinite(value);
    }
}
