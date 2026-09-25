namespace DdsRecolor.Core.Recolor;

using DdsRecolor.Core.Formats;

/// <summary>How alpha is encoded, which determines whether colour needs un-premultiplying.</summary>
public enum AlphaMode
{
    /// <summary>Colour is independent of alpha. The common case.</summary>
    Straight,

    /// <summary>Colour has already been multiplied by alpha; un-premultiply before shifting.</summary>
    Premultiplied,
}

/// <summary>Options controlling how a source colour is mapped toward a target colour.</summary>
public sealed class RecolorOptions
{
    /// <summary>The colour to look for and remap.</summary>
    public required Rgb Source { get; init; }

    /// <summary>The colour to remap it toward.</summary>
    public required Rgb Target { get; init; }

    /// <summary>
    /// Master strength of the whole adjustment, in [0,1]. Scales hue,
    /// saturation and brightness together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a master dial over all three channels, so <b>zero means no change
    /// at all</b> and the output is byte-identical to the input. That guarantee is
    /// depended on by the test suite to prove the block rewriting itself cannot
    /// alter bytes, independently of any colour maths. It also matches what a user
    /// expects from a dial turned to zero.
    /// </para>
    /// <para>
    /// Negative values invert the hue rotation, which is occasionally useful for
    /// exploring a colour space. Negative values do not invert saturation or
    /// brightness, because a negative saturation is meaningless.
    /// </para>
    /// <para>
    /// For per-channel control, use <see cref="SaturationStrength"/> and
    /// <see cref="BrightnessStrength"/> instead; they compose with this one.
    /// </para>
    /// </remarks>
    public double Intensity { get; init; } = 1.0;

    /// <summary>How alpha is encoded in the texture.</summary>
    public AlphaMode AlphaMode { get; init; } = AlphaMode.Straight;

    /// <summary>
    /// Keep each pixel's own saturation instead of moving it toward the target's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default, meaning saturation <b>is</b> adjusted so the result matches
    /// the target colour. Turn this on to pin saturation to the source image
    /// instead, which preserves the texture's vividness at the cost of not fully
    /// reaching the target.
    /// </para>
    /// <para>
    /// The adjustment is a single multiplicative factor applied to every pixel,
    /// not an assignment of the target's saturation, so relative contrast between
    /// texels survives. See <see cref="SaturationStrength"/>.
    /// </para>
    /// </remarks>
    public bool RetainSaturation { get; init; }

    /// <summary>
    /// How far to move saturation toward the target's, in [0,1]. Ignored when
    /// <see cref="RetainSaturation"/> is set. 1 means match the target's ratio,
    /// 0 leaves saturation untouched.
    /// </summary>
    public double SaturationStrength { get; init; } = 1.0;

    /// <summary>
    /// Keep each pixel's own brightness instead of moving it toward the target's.
    /// </summary>
    /// <remarks>
    /// Off by default, meaning brightness <b>is</b> adjusted to match the target.
    /// Turn this on to pin brightness to the source image, which preserves the
    /// original lighting and is the safer choice for a texture whose brightness
    /// carries important detail.
    /// </remarks>
    public bool RetainBrightness { get; init; }

    /// <summary>
    /// How far to move brightness toward the target's, in [0,1]. Ignored when
    /// <see cref="RetainBrightness"/> is set.
    /// </summary>
    public double BrightnessStrength { get; init; } = 1.0;

    /// <summary>
    /// How far a pixel's hue may sit from the source hue and still be shifted,
    /// in degrees. Pixels outside this window are left alone.
    /// </summary>
    /// <remarks>
    /// The window is widened by the hue-to-saturation coupling: a nearly grey
    /// pixel has an unstable hue, so hue distance alone is a poor gate. See
    /// <see cref="HueShift.IsInSourceHueWindow"/>.
    /// </remarks>
    public double HueToleranceDegrees { get; init; } = 60.0;

    /// <summary>
    /// Pixels with saturation below this are treated as greyscale and never
    /// shifted, so shadows and highlights do not pick up a colour cast.
    /// </summary>
    public double SaturationFloor { get; init; } = 0.08;

    /// <summary>
    /// Feather width above <see cref="SaturationFloor"/>, in saturation units,
    /// over which the shift ramps in from 0 to full strength.
    /// </summary>
    public double SaturationFeather { get; init; } = 0.10;
}

/// <summary>
/// Applies a source-to-target colour adjustment: hue, plus saturation and
/// brightness, so the source colour lands as close to the target as the format
/// allows while preserving relative shading.
/// </summary>
/// <remarks>
/// <para>
/// One global set of adjustment factors is computed from the source and target
/// colours and applied to every qualifying pixel. That uniformity is deliberate:
/// it keeps neighbouring BC1 endpoints proportional to each other, which is what
/// stops a 4x4 tiling pattern appearing once the blocks are re-quantised.
/// </para>
/// <para>
/// <b>All three channels are adjusted by default</b>, because source and target
/// colours usually differ in saturation and brightness as much as in hue. A
/// source of <c>#803333</c> (S=0.60, V=0.50) and a target of <c>#D89D83</c>
/// (S=0.39, V=0.85) differ by only 18 degrees of hue but by 21% and 35% of the
/// saturation and value ranges. Adjusting hue alone produced <c>#804B33</c>, a
/// dark saturated red that still read as the original colour, and raising
/// <c>Intensity</c> did not help: it rotated the hue further past the target
/// instead of closing the gap.
/// </para>
/// <code>
///   pixel.H' = pixel.H + (target.H - source.H) * intensity
///   pixel.S' = pixel.S * (target.S / source.S) ^ saturationStrength
///   pixel.V' = pixel.V * (target.V / source.V) ^ brightnessStrength
/// </code>
/// <para>
/// <see cref="RecolorOptions.RetainSaturation"/> and
/// <see cref="RecolorOptions.RetainBrightness"/> pin a channel to the source
/// image instead. That is worth doing when the channel carries detail worth
/// protecting: brightness in particular encodes lighting and geometry, so a
/// texture whose shading matters can be recoloured in hue without altering how
/// light falls across it.
/// </para>
/// <para>
/// Because each factor is shared across all pixels, relative differences between
/// texels survive: a pixel twice as saturated as its neighbour stays twice as
/// saturated. Shading is scaled rather than flattened. Factors are clamped, so a
/// pixel already at full saturation or brightness saturates instead of wrapping.
/// </para>
/// <para>
/// Because the factor is shared, relative differences between texels survive:
/// a pixel twice as saturated as its neighbour stays twice as saturated. Shading
/// is scaled rather than flattened. Both factors are clamped, so a pixel already
/// at full saturation or full brightness saturates instead of wrapping.
/// </para>
/// </remarks>
public sealed class HueShift
{
    private readonly RecolorOptions _options;
    private readonly double _hueDelta;
    private readonly double _saturationFactor;
    private readonly double _valueFactor;

    public HueShift(RecolorOptions options)
    {
        _options = options;

        var sourceHsv = Hsv.FromRgb(options.Source);
        var targetHsv = Hsv.FromRgb(options.Target);

        // Intensity is a master dial over all three channels, so it scales the
        // per-channel strengths as well as the hue delta. That keeps a value of
        // zero meaning "change nothing", which the test suite relies on to prove
        // the block rewriting is itself harmless.
        //
        // Only the non-negative part scales the channel factors: a negative
        // intensity is a meaningful way to invert a hue rotation, but a negative
        // saturation or brightness scale is not.
        double channelScale = Math.Max(0.0, options.Intensity);

        _hueDelta = Hsv.HueDelta(sourceHsv.H, targetHsv.H) * options.Intensity;

        _saturationFactor = ComputeFactor(
            targetHsv.S, sourceHsv.S,
            retain: options.RetainSaturation,
            options.SaturationStrength * channelScale);

        _valueFactor = ComputeFactor(
            targetHsv.V, sourceHsv.V,
            retain: options.RetainBrightness,
            options.BrightnessStrength * channelScale);
    }

    /// <summary>
    /// Ratio to multiply pixels by, raised to <paramref name="strength"/>.
    /// </summary>
    /// <remarks>
    /// Returns 1 (no change) when the channel is being retained, the strength is
    /// zero, or the source channel is effectively zero. The last case matters: a
    /// source with zero saturation or zero value makes the ratio infinite or
    /// undefined, and there is no meaningful scaling to apply, so leaving pixels
    /// alone is better than clamping every one of them to the target.
    /// </remarks>
    private static double ComputeFactor(double target, double source, bool retain, double strength)
    {
        if (retain || strength <= 0.0) return 1.0;
        if (source <= 1e-6) return 1.0;

        double ratio = target / source;
        return Math.Pow(ratio, strength);
    }

    /// <summary>The signed hue rotation applied to qualifying pixels, in degrees.</summary>
    public double HueDelta => _hueDelta;

    /// <summary>
    /// True when the transform provably cannot change any pixel.
    /// </summary>
    /// <remarks>
    /// A zero hue delta is not enough to call this a no-op any more, because the
    /// saturation and brightness factors can still be adjusting pixels.
    /// </remarks>
    public bool IsNoOp =>
        (_hueDelta == 0.0 && _saturationFactor == 1.0 && _valueFactor == 1.0)
        || _options.SaturationFloor >= 1.0;

    /// <summary>
    /// Shift a single colour, returning it unchanged when it falls outside the
    /// selective window.
    /// </summary>
    public Rgb Apply(Rgb colour) => ApplyWithWeight(colour, out _);

    /// <summary>
    /// Shift a colour and report the weight that was applied, in [0,1]. A weight
    /// of zero means the colour was left untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The weight blends between the original and fully shifted colour rather
    /// than scaling the hue delta, so a partial shift of a low-saturation pixel
    /// cannot drift its hue unpredictably.
    /// </para>
    /// <para>
    /// Hue, saturation and brightness are applied together before the blend, so
    /// a partially-weighted pixel moves toward the full result rather than
    /// toward a half-applied mixture of the three.
    /// </para>
    /// </remarks>
    public Rgb ApplyWithWeight(Rgb colour, out double weight)
    {
        weight = 0.0;
        if (IsNoOp) return colour;

        var hsv = Hsv.FromRgb(colour);

        if (!IsInSourceHueWindow(hsv.H)) return colour;

        // Ramp in from the saturation floor so near-grey pixels keep their
        // neutrality instead of snapping to a shifted colour.
        double saturationWeight = Smoothstep(
            _options.SaturationFloor,
            _options.SaturationFloor + _options.SaturationFeather,
            hsv.S);

        if (saturationWeight <= 0.0) return colour;

        weight = saturationWeight;
        Rgb shifted = Transform(hsv);

        if (saturationWeight >= 1.0) return shifted;

        return new Rgb(
            Blend(colour.R, shifted.R, saturationWeight),
            Blend(colour.G, shifted.G, saturationWeight),
            Blend(colour.B, shifted.B, saturationWeight));
    }

    /// <summary>
    /// Apply the hue rotation and the optional saturation and brightness factors
    /// to an HSV colour.
    /// </summary>
    /// <remarks>
    /// Saturation and value are scaled by a shared multiplier and clamped to
    /// [0,1]. Clamping rather than wrapping is what makes a pixel that is already
    /// fully saturated simply stay saturated, instead of folding back to a duller
    /// colour. It also means highlights can clip when brightening, which is the
    /// expected behaviour for a brightness increase.
    /// </remarks>
    private Rgb Transform(Hsv hsv)
    {
        double saturation = hsv.S;
        double value = hsv.V;

        if (_saturationFactor != 1.0)
        {
            saturation = Math.Clamp(saturation * _saturationFactor, 0.0, 1.0);
        }

        if (_valueFactor != 1.0)
        {
            value = Math.Clamp(value * _valueFactor, 0.0, 1.0);
        }

        return new Hsv(hsv.RotateHue(_hueDelta).H, saturation, value).ToRgb();
    }

    /// <summary>
    /// True when <paramref name="hue"/> is within the tolerance window of the
    /// source hue. The window wraps around 0/360.
    /// </summary>
    /// <remarks>
    /// Only the hue distance is tested here. Saturation is handled separately by
    /// the ramp in <see cref="ApplyWithWeight"/>, because hue is numerically
    /// unstable for desaturated pixels: a 1/255 change in a grey pixel's blue
    /// channel can swing its hue by 180 degrees. Gating on hue alone would
    /// therefore either reject legitimate dark source pixels or admit arbitrary
    /// greys, so saturation must carry part of the decision.
    /// </remarks>
    public bool IsInSourceHueWindow(double hue)
    {
        double sourceHue = Hsv.FromRgb(_options.Source).H;
        return Math.Abs(Hsv.HueDelta(sourceHue, hue)) <= _options.HueToleranceDegrees;
    }

    private static double Smoothstep(double edge0, double edge1, double x)
    {
        if (edge1 <= edge0) return x >= edge1 ? 1.0 : 0.0;
        double t = (x - edge0) / (edge1 - edge0);
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;
        return t * t * (3.0 - 2.0 * t);
    }

    private static byte Blend(byte from, byte to, double weight)
    {
        double value = from + ((to - from) * weight);
        if (value <= 0.0) return 0;
        if (value >= 255.0) return 255;
        return (byte)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
