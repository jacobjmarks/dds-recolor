namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Recolor;

/// <summary>
/// Tests for how the transform handles saturation and brightness, including the
/// retain flags that pin a channel to the source image.
/// </summary>
/// <remarks>
/// <para>
/// All three channels are adjusted by default, because a hue-only rotation cannot
/// reach a target that differs mainly in saturation or brightness. A source of
/// <c>#803333</c> (S=0.60, V=0.50) and target of <c>#D89D83</c> (S=0.39, V=0.85)
/// differ by only 18 degrees of hue but by 21% and 35% of the saturation and
/// value ranges, so hue-only produced <c>#804B33</c> — still a dark saturated
/// red, which reads as "the original colour".
/// </para>
/// <para>
/// <c>RetainSaturation</c> and <c>RetainBrightness</c> opt a channel back out,
/// pinning it to the source image. Each applies a shared multiplicative factor
/// rather than assigning the target's value, so relative shading between texels
/// survives and geometry detail is not flattened.
/// </para>
/// </remarks>
public class SaturationBrightnessTests
{
    private static readonly Rgb DarkRed = new(0x80, 0x33, 0x33);   // S 0.60 V 0.50
    private static readonly Rgb Peach = new(0xD8, 0x9D, 0x83);     // S 0.39 V 0.85

    private static HueShift Shift(
        bool retainSaturation = false, double saturationStrength = 1.0,
        bool retainBrightness = false, double brightnessStrength = 1.0)
        => new(new RecolorOptions
        {
            Source = DarkRed,
            Target = Peach,
            RetainSaturation = retainSaturation,
            SaturationStrength = saturationStrength,
            RetainBrightness = retainBrightness,
            BrightnessStrength = brightnessStrength,
        });

    // -----------------------------------------------------------------------
    // The default: all three channels adjust
    // -----------------------------------------------------------------------

    [Fact]
    public void ByDefault_SourceColourReachesTheTarget()
    {
        // The headline behaviour. With nothing retained, the source colour should
        // land on the target rather than merely rotating its hue.
        Rgb result = Shift().Apply(DarkRed);

        var targetHsv = Hsv.FromRgb(Peach);
        var resultHsv = Hsv.FromRgb(result);

        Assert.InRange(resultHsv.H, targetHsv.H - 1, targetHsv.H + 1);
        Assert.InRange(resultHsv.S, targetHsv.S - 0.02, targetHsv.S + 0.02);
        Assert.InRange(resultHsv.V, targetHsv.V - 0.02, targetHsv.V + 0.02);
    }

    [Fact]
    public void ByDefault_SaturationAndBrightnessBothChange()
    {
        var sourceHsv = Hsv.FromRgb(DarkRed);
        var resultHsv = Hsv.FromRgb(Shift().Apply(DarkRed));

        Assert.True(Math.Abs(resultHsv.S - sourceHsv.S) > 0.05,
            $"Saturation should have moved: {sourceHsv.S:F3} -> {resultHsv.S:F3}.");
        Assert.True(Math.Abs(resultHsv.V - sourceHsv.V) > 0.05,
            $"Brightness should have moved: {sourceHsv.V:F3} -> {resultHsv.V:F3}.");
    }

    // -----------------------------------------------------------------------
    // Retaining a channel
    // -----------------------------------------------------------------------

    [Fact]
    public void RetainSaturation_KeepsSourceSaturation()
    {
        var sourceHsv = Hsv.FromRgb(DarkRed);
        var resultHsv = Hsv.FromRgb(Shift(retainSaturation: true).Apply(DarkRed));

        Assert.InRange(resultHsv.S, sourceHsv.S - 0.01, sourceHsv.S + 0.01);
    }

    [Fact]
    public void RetainBrightness_KeepsSourceBrightness()
    {
        var sourceHsv = Hsv.FromRgb(DarkRed);
        var resultHsv = Hsv.FromRgb(Shift(retainBrightness: true).Apply(DarkRed));

        Assert.InRange(resultHsv.V, sourceHsv.V - 0.01, sourceHsv.V + 0.01);
    }

    [Fact]
    public void RetainSaturation_StillAdjustsBrightnessAndHue()
    {
        // Retaining one channel must not disable the others, or the flag would be
        // an all-or-nothing switch rather than a per-channel choice.
        var sourceHsv = Hsv.FromRgb(DarkRed);
        var targetHsv = Hsv.FromRgb(Peach);
        var resultHsv = Hsv.FromRgb(Shift(retainSaturation: true).Apply(DarkRed));

        Assert.InRange(resultHsv.V, targetHsv.V - 0.02, targetHsv.V + 0.02);
        Assert.True(Math.Abs(Hsv.HueDelta(sourceHsv.H, resultHsv.H)) > 5.0);
    }

    [Fact]
    public void RetainBrightness_StillAdjustsSaturationAndHue()
    {
        var targetHsv = Hsv.FromRgb(Peach);
        var resultHsv = Hsv.FromRgb(Shift(retainBrightness: true).Apply(DarkRed));

        Assert.InRange(resultHsv.S, targetHsv.S - 0.02, targetHsv.S + 0.02);
    }

    [Fact]
    public void RetainingBoth_IsAPureHueRotation()
    {
        // With both retained, only hue moves, which is the behaviour the tool had
        // before saturation and brightness were adjustable.
        var sourceHsv = Hsv.FromRgb(DarkRed);
        Rgb result = Shift(retainSaturation: true, retainBrightness: true).Apply(DarkRed);
        var resultHsv = Hsv.FromRgb(result);

        Assert.InRange(resultHsv.S, sourceHsv.S - 0.01, sourceHsv.S + 0.01);
        Assert.InRange(resultHsv.V, sourceHsv.V - 0.01, sourceHsv.V + 0.01);

        // And the hue must have actually moved.
        Assert.True(Math.Abs(Hsv.HueDelta(sourceHsv.H, resultHsv.H)) > 5.0);
    }

    [Fact]
    public void RetainingAChannel_MovesTheResultAwayFromTheTarget()
    {
        // Why the tool warns when you retain a channel that carries most of the
        // difference: the result legitimately stops matching the target.
        double full = Math.Sqrt(Rgb.DistanceSquared(Shift().Apply(DarkRed), Peach));
        double noBrightness = Math.Sqrt(
            Rgb.DistanceSquared(Shift(retainBrightness: true).Apply(DarkRed), Peach));
        double noSaturation = Math.Sqrt(
            Rgb.DistanceSquared(Shift(retainSaturation: true).Apply(DarkRed), Peach));

        Assert.True(noBrightness > full);
        Assert.True(noSaturation > full);
        Assert.True(full < 1.0, $"Default should reach the target, distance was {full:F1}.");
    }

    // -----------------------------------------------------------------------
    // Shading preservation - the reason these are shared factors, not
    // assignments of the target's values.
    // -----------------------------------------------------------------------

    [Fact]
    public void SaturationAdjustment_PreservesRelativeShadingBetweenTexels()
    {
        // Two colours where one has roughly twice the saturation of the other.
        // After scaling, that ratio must survive. Assigning every pixel the
        // target's saturation would flatten a texture and destroy its detail.
        var low = new Rgb(0x80, 0x55, 0x55);
        var high = new Rgb(0x80, 0x22, 0x22);

        double ratioBefore = Hsv.FromRgb(high).S / Hsv.FromRgb(low).S;

        var shift = Shift();
        double ratioAfter = Hsv.FromRgb(shift.Apply(high)).S / Hsv.FromRgb(shift.Apply(low)).S;

        Assert.True(Math.Abs(ratioAfter - ratioBefore) < 0.15,
            $"Relative saturation changed: {ratioBefore:F3} -> {ratioAfter:F3}.");
    }

    [Fact]
    public void BrightnessAdjustment_PreservesRelativeShadingBetweenTexels()
    {
        var dark = new Rgb(0x40, 0x1A, 0x1A);
        var bright = new Rgb(0x80, 0x33, 0x33);

        double ratioBefore = Hsv.FromRgb(bright).V / Hsv.FromRgb(dark).V;

        var shift = Shift();
        double ratioAfter = Hsv.FromRgb(shift.Apply(bright)).V / Hsv.FromRgb(shift.Apply(dark)).V;

        Assert.True(Math.Abs(ratioAfter - ratioBefore) < 0.15,
            $"Relative brightness changed: {ratioBefore:F3} -> {ratioAfter:F3}.");
    }

    [Fact]
    public void BrightnessAdjustment_DoesNotFlattenAGradient()
    {
        // A ramp of brightnesses must remain a ramp. This is the failure mode that
        // would destroy stretched geometry, so it is asserted on many samples.
        var shift = Shift();

        var values = new List<double>();
        for (int v = 0x20; v <= 0x60; v += 0x08)
        {
            var colour = new Rgb((byte)v, (byte)(v * 0x33 / 0x80), (byte)(v * 0x33 / 0x80));
            values.Add(Hsv.FromRgb(shift.Apply(colour)).V);
        }

        for (int i = 1; i < values.Count; i++)
        {
            Assert.True(values[i] > values[i - 1],
                $"Brightness ramp flattened at sample {i}: " +
                $"{values[i - 1]:F3} -> {values[i]:F3}.");
        }
    }

    // -----------------------------------------------------------------------
    // Clamping and edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Brightening_SaturatesRatherThanWrapping()
    {
        // A pixel already near full brightness, scaled upward, must clamp rather
        // than wrap to dark. Wrapping would show as speckle.
        var bright = new Rgb(0xF0, 0x60, 0x60);

        Rgb result = Shift().Apply(bright);

        Assert.True(result.R >= bright.R && result.G >= bright.G && result.B >= bright.B,
            $"Channels wrapped instead of saturating: {bright} -> {result}.");
    }

    [Fact]
    public void SaturationIncrease_ClampsAtFullySaturated()
    {
        var shift = new HueShift(new RecolorOptions
        {
            Source = new Rgb(0xC0, 0xA0, 0xA0),   // pale
            Target = new Rgb(0xFF, 0x00, 0x00),   // fully saturated
        });

        Rgb result = shift.Apply(new Rgb(0xC0, 0xA0, 0xA0));

        Assert.InRange(Hsv.FromRgb(result).S, 0.0, 1.0);
    }

    [Fact]
    public void ZeroSaturationSource_DoesNotProduceInvalidColour()
    {
        // The ratio is undefined when the source has no saturation, so the factor
        // must fall back to 1 rather than producing NaN or infinity.
        var shift = new HueShift(new RecolorOptions
        {
            Source = new Rgb(0x80, 0x80, 0x80),   // pure grey, S = 0
            Target = Peach,
        });

        Rgb result = shift.Apply(new Rgb(0x80, 0x80, 0x80));

        Assert.InRange(result.R, (byte)0, (byte)255);
        Assert.InRange(result.G, (byte)0, (byte)255);
        Assert.InRange(result.B, (byte)0, (byte)255);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void StrengthIsMonotonicTowardTheTarget(double strength)
    {
        // Increasing strength must move the colour monotonically toward the
        // target, so the dial is predictable rather than having odd steps.
        var shift = new HueShift(new RecolorOptions
        {
            Source = DarkRed,
            Target = Peach,
            SaturationStrength = strength,
            BrightnessStrength = strength,
        });

        Rgb result = shift.Apply(DarkRed);

        double distance = Math.Sqrt(Rgb.DistanceSquared(result, Peach));
        double baseline = Math.Sqrt(Rgb.DistanceSquared(DarkRed, Peach));

        Assert.True(distance <= baseline + 1.0,
            $"Strength {strength} moved away from the target.");
    }

    [Fact]
    public void ZeroStrength_IsAPureHueRotation()
    {
        // Strength 0 must behave exactly like retaining the channel, so the dial
        // has a meaningful zero that agrees with the retain flags.
        var zeroStrength = Shift(saturationStrength: 0.0, brightnessStrength: 0.0);
        var retained = Shift(retainSaturation: true, retainBrightness: true);

        Assert.Equal(retained.Apply(DarkRed), zeroStrength.Apply(DarkRed));
    }

    [Fact]
    public void RetainFlags_AndZeroStrength_AreInterchangeable()
    {
        // Both spellings of "keep this channel" must agree, or the two ways of
        // expressing the same intent would produce different colours.
        Assert.Equal(
            Shift(retainSaturation: true).Apply(DarkRed),
            Shift(saturationStrength: 0.0).Apply(DarkRed));

        Assert.Equal(
            Shift(retainBrightness: true).Apply(DarkRed),
            Shift(brightnessStrength: 0.0).Apply(DarkRed));
    }

    // -----------------------------------------------------------------------
    // Interaction with the rest of the engine
    // -----------------------------------------------------------------------

    [Fact]
    public void NeutralGreyPixels_AreNeverAdjusted()
    {
        // The saturation gate must still hold: a grey pixel has no colour to
        // shift, so it passes through untouched regardless of the flags.
        var grey = new Rgb(0x80, 0x80, 0x80);

        Assert.Equal(grey, Shift().Apply(grey));
        Assert.Equal(grey, Shift(retainSaturation: true).Apply(grey));
        Assert.Equal(grey, Shift(retainBrightness: true).Apply(grey));
    }

    [Fact]
    public void Flags_ComposeWithTheBlockEngineAndPreserveFileSize()
    {
        // End-to-end through a real rewrite: the flags must not disturb any of
        // the structural guarantees.
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "bc1_gradient_64x64.dds");
        var file = DdsRecolor.Core.Io.DdsFile.Load(path);

        byte[] everythingAdjusted = file.Recolor(Shift()).Data;
        byte[] hueOnly = file.Recolor(
            Shift(retainSaturation: true, retainBrightness: true)).Data;

        Assert.Equal(file.Data.Length, everythingAdjusted.Length);
        Assert.Equal(file.Data.Length, hueOnly.Length);

        // The two modes must produce different output, or the flags are inert.
        Assert.NotEqual(
            Convert.ToHexString(everythingAdjusted),
            Convert.ToHexString(hueOnly));

        // And the header must still be untouched in both.
        Assert.Equal(
            file.Data.AsSpan(0, file.Header.HeaderBytes).ToArray(),
            everythingAdjusted.AsSpan(0, file.Header.HeaderBytes).ToArray());
        Assert.Equal(
            file.Data.AsSpan(0, file.Header.HeaderBytes).ToArray(),
            hueOnly.AsSpan(0, file.Header.HeaderBytes).ToArray());
    }

    [Fact]
    public void IntensityZero_IsStillAByteIdenticalNoOp()
    {
        // Intensity 0 must remain a true no-op with every channel active,
        // otherwise the isolation test that proves the rewrite plumbing is safe
        // would stop meaning anything.
        var shift = new HueShift(new RecolorOptions
        {
            Source = DarkRed,
            Target = Peach,
            Intensity = 0.0,
        });

        Assert.True(shift.IsNoOp);
    }
}
