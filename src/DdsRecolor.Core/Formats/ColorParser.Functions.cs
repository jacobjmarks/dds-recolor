namespace DdsRecolor.Core.Formats;

/// <summary>
/// Parsing for the functional syntaxes: <c>rgb()</c>, <c>hsl()</c>,
/// <c>hsv()</c>, <c>hwb()</c> and <c>lab()</c>, in both parenthesised and bare
/// forms.
/// </summary>
public static partial class ColorParser
{
    private static bool TryParseFunction(string value, out ParsedColor parsed)
    {
        parsed = default;

        string lowered = value.ToLowerInvariant();

        // Locate the keyword and the component text, accepting either
        // "name(...)" or the bare "name a b c" form.
        ColorFormat format;
        string body;

        int open = lowered.IndexOf('(');
        if (open >= 0)
        {
            if (!lowered.EndsWith(')')) return false;
            format = KeywordToFormat(lowered[..open].Trim());
            if (format == ColorFormat.Unknown) return false;
            body = value[(open + 1)..^1];
        }
        else
        {
            int space = lowered.IndexOf(' ');
            if (space <= 0) return false;
            format = KeywordToFormat(lowered[..space]);
            if (format == ColorFormat.Unknown) return false;
            body = value[(space + 1)..];
        }

        string[] parts = SplitComponents(body);
        if (parts.Length < 3 || parts.Length > 4) return false;

        // A fourth component is alpha. Only fully opaque alpha is accepted,
        // because the engine shifts colour and has no opacity channel to write.
        if (parts.Length == 4 && !IsOpaqueAlpha(parts[3])) return false;

        return format switch
        {
            ColorFormat.Rgb => TryBuildRgb(parts, out parsed),
            ColorFormat.Hsl => TryBuildHsl(parts, out parsed),
            ColorFormat.Hsv => TryBuildHsv(parts, out parsed),
            ColorFormat.Hwb => TryBuildHwb(parts, out parsed),
            ColorFormat.Lab => TryBuildLab(parts, out parsed),
            _ => false,
        };
    }

    private static ColorFormat KeywordToFormat(string keyword) => keyword switch
    {
        "rgb" or "rgba" => ColorFormat.Rgb,
        "hsl" or "hsla" => ColorFormat.Hsl,
        "hsv" or "hsva" => ColorFormat.Hsv,
        "hwb" or "hwba" => ColorFormat.Hwb,
        "lab" => ColorFormat.Lab,
        _ => ColorFormat.Unknown,
    };

    /// <summary>
    /// Split a function body into components.
    /// </summary>
    /// <remarks>
    /// Handles the comma-separated legacy syntax and the whitespace-separated
    /// modern one, plus the <c>/</c> alpha separator with or without surrounding
    /// spaces. A <c>/</c> is treated as a separator so the result is always a
    /// flat component list.
    /// </remarks>
    private static string[] SplitComponents(string body)
    {
        var components = new List<string>();
        var current = new System.Text.StringBuilder();

        void Flush()
        {
            string part = current.ToString().Trim();
            if (part.Length > 0) components.Add(part);
            current.Clear();
        }

        foreach (char c in body)
        {
            if (c is ',' or '/' || char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return [.. components];
    }

    /// <summary>True when an alpha component represents fully opaque.</summary>
    private static bool IsOpaqueAlpha(string text)
    {
        if (text.EndsWith('%'))
        {
            return TryParseNumber(text[..^1], out double percent)
                && Math.Abs(percent - 100.0) < 1e-6;
        }

        return TryParseNumber(text, out double value) && Math.Abs(value - 1.0) < 1e-6;
    }
}
