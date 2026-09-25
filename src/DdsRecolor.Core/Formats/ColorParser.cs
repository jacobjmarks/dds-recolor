namespace DdsRecolor.Core.Formats;

/// <summary>The colour syntaxes <see cref="ColorParser"/> accepts.</summary>
public enum ColorFormat
{
    Unknown,
    Hex,
    Named,
    Rgb,
    Hsl,
    Hsv,
    Hwb,
    Lab,
}

/// <summary>Details of a successfully parsed colour, for reporting and tests.</summary>
public readonly record struct ParsedColor(Rgb Color, ColorFormat Format);

/// <summary>
/// Parses colour strings supplied on the command line.
/// </summary>
/// <remarks>
/// <para>Supported syntaxes, all case-insensitive and whitespace-tolerant:</para>
/// <code>
///   hex         #8B5A2B   #8b5a2b   8B5A2B   0x8B5A2B   #FA0   (3-digit short form)
///   hex+alpha   #8B5A2BFF                                       (alpha must be opaque)
///   named       tomato    sienna    rebeccapurple
///   rgb()       rgb(139, 90, 43)      rgba(139 90 43 / 100%)
///   rgb() 0-1   rgb(0.55 0.35 0.17)
///   hsl()       hsl(29, 53%, 36%)     hsl(29deg 53% 36%)
///   hsv()       hsv(29, 53%, 55%)
///   hwb()       hwb(29 17% 45%)
///   lab()       lab(45 18 32)         lab(45% 18 32)
///   bare        rgb 139 90 43         hsl 29 53% 36%
/// </code>
/// <para>
/// Three conventions are auto-detected rather than required explicitly, so values
/// can be pasted from other tools:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>rgb ranges.</b> If any channel exceeds 1, all three are read as 0-255;
/// otherwise as 0-1. Percentages always mean 0-100.
/// </item>
/// <item>
/// <b>hue units.</b> A bare number is degrees; <c>deg</c>, <c>grad</c>,
/// <c>rad</c> and <c>turn</c> suffixes are honoured, and hue wraps.
/// </item>
/// <item>
/// <b>percentages.</b> Accepted wherever a 0-1 channel is expected, so
/// <c>50%</c> and <c>0.5</c> are equivalent for saturation, value and lightness.
/// </item>
/// </list>
/// <para>
/// Out-of-range values are <b>rejected</b> rather than clamped. A silently
/// clamped <c>rgb(300, 0, 0)</c> would look like a working command while
/// producing a colour the user did not ask for, which is harder to notice than
/// an error.
/// </para>
/// </remarks>
public static partial class ColorParser
{
    /// <summary>Human-readable list of everything accepted, for error messages.</summary>
    public const string SupportedFormats =
        "#8B5A2B, #FA0, 8B5A2B, tomato, rgb(139,90,43), rgb(0.55 0.35 0.17), " +
        "hsl(29 53% 36%), hsv(29 53% 55%), hwb(29 17% 45%), lab(45 18 32)";

    /// <summary>Parse a colour, throwing a helpful error when it is not recognised.</summary>
    /// <exception cref="FormatException">The text is not a supported colour syntax.</exception>
    public static Rgb Parse(string text)
    {
        if (TryParse(text, out Rgb colour)) return colour;
        throw new FormatException(Describe(text, indent: false));
    }

    /// <summary>Parse a colour and report which syntax was recognised.</summary>
    public static bool TryParse(string? text, out ParsedColor parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string value = text.Trim();

        if (TryParseHex(value, out Rgb hex))
        {
            parsed = new ParsedColor(hex, ColorFormat.Hex);
            return true;
        }

        if (NamedColors.TryGet(value, out Rgb named))
        {
            parsed = new ParsedColor(named, ColorFormat.Named);
            return true;
        }

        if (TryParseFunction(value, out ParsedColor functional))
        {
            parsed = functional;
            return true;
        }

        return false;
    }

    /// <summary>Parse a colour without caring which syntax it used.</summary>
    public static bool TryParse(string? text, out Rgb colour)
    {
        colour = default;
        if (!TryParse(text, out ParsedColor parsed)) return false;
        colour = parsed.Color;
        return true;
    }

    /// <summary>
    /// Build the message shown when a colour cannot be parsed, including a typo
    /// suggestion when one is available.
    /// </summary>
    internal static string Describe(string text, bool indent)
    {
        string prefix = indent ? "  " : string.Empty;
        var lines = new List<string>
        {
            $"'{text}' is not a valid colour.",
            $"{prefix}Expected one of: {SupportedFormats}",
        };

        var suggestions = NameSuggester.Suggest(text);
        if (suggestions.Count > 0)
        {
            lines.Add($"{prefix}Did you mean: {string.Join(", ", suggestions)}?");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
