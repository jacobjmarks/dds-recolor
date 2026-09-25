namespace DdsRecolor.Core.Formats;

/// <summary>
/// An HWB colour: hue, whiteness and blackness, as used by CSS.
/// </summary>
/// <param name="H">Hue in degrees, [0,360).</param>
/// <param name="W">Whiteness, [0,1]. Fraction of pure white mixed in.</param>
/// <param name="B">Blackness, [0,1]. Fraction of pure black mixed in.</param>
/// <remarks>
/// <para>
/// HWB is a simple, human-friendly model: you pick a hue, then say how much
/// white and how much black to mix in. It is often easier to reason about than
/// HSL when the goal is "a pale version of this red" or "a dark version", which
/// is exactly the kind of target this tool is asked for.
/// </para>
/// <para>
/// It is defined only for displayable colours, so the same conversion used by CSS
/// is applied: if whiteness plus blackness exceeds 1, the two are scaled down
/// proportionally. That keeps every input meaningful rather than rejecting it.
/// </para>
/// </remarks>
public readonly record struct Hwb(double H, double W, double B)
{
    /// <summary>Convert an sRGB colour to HWB.</summary>
    public static Hwb FromRgb(Rgb colour)
    {
        var hsv = Hsv.FromRgb(colour);
        double whiteness = (1.0 - hsv.S) * hsv.V;
        double blackness = 1.0 - hsv.V;
        return new Hwb(hsv.H, whiteness, blackness);
    }

    /// <summary>Convert HWB to sRGB.</summary>
    /// <remarks>
    /// Implemented via the HSV relationship rather than by mixing white and black
    /// in RGB space, because the two are equivalent mathematically and going
    /// through HSV reuses one conversion instead of duplicating the hue logic.
    /// </remarks>
    public Rgb ToRgb()
    {
        double whiteness = W;
        double blackness = B;

        // CSS scales the two down proportionally when they sum above 1, rather
        // than treating the input as invalid.
        double sum = whiteness + blackness;
        if (sum > 1.0)
        {
            whiteness /= sum;
            blackness /= sum;
        }

        double value = 1.0 - blackness;
        double saturation = value <= 0.0 ? 0.0 : 1.0 - (whiteness / value);

        return new Hsv(H, Math.Clamp(saturation, 0.0, 1.0), Math.Clamp(value, 0.0, 1.0)).ToRgb();
    }

    public override string ToString() => $"hwb({H:F1} {W * 100:F1}% {B * 100:F1}%)";
}
