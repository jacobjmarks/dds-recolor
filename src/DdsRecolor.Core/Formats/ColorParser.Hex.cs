namespace DdsRecolor.Core.Formats;

/// <summary>
/// Hex parsing, kept separate from the functional syntaxes so each remains
/// small enough to read in one go.
/// </summary>
public static partial class ColorParser
{
    private static bool TryParseHex(string value, out Rgb colour)
    {
        colour = default;

        bool hadPrefix = false;
        if (value.StartsWith('#'))
        {
            value = value[1..];
            hadPrefix = true;
        }
        else if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
            hadPrefix = true;
        }

        // Without a prefix, require the whole string to be hex digits so that a
        // colour word is not mistaken for a hex value.
        if (!hadPrefix && !IsAllHexDigits(value)) return false;

        // Accept an 8-digit form only when the alpha byte is fully opaque, so a
        // genuine mistake is reported rather than silently reinterpreted.
        if (value.Length == 8)
        {
            if (!value.EndsWith("FF", StringComparison.OrdinalIgnoreCase)) return false;
            value = value[..6];
        }

        if (value.Length == 3)
        {
            // Expand #rgb to #rrggbb by doubling each nibble.
            value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
        }

        if (value.Length != 6 || !IsAllHexDigits(value)) return false;

        if (!TryParseHexByte(value.AsSpan(0, 2), out byte r)) return false;
        if (!TryParseHexByte(value.AsSpan(2, 2), out byte g)) return false;
        if (!TryParseHexByte(value.AsSpan(4, 2), out byte b)) return false;

        colour = new Rgb(r, g, b);
        return true;
    }

    private static bool IsAllHexDigits(string value)
    {
        if (value.Length == 0) return false;
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }

    private static bool TryParseHexByte(ReadOnlySpan<char> text, out byte result) =>
        byte.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                      System.Globalization.CultureInfo.InvariantCulture, out result);
}
