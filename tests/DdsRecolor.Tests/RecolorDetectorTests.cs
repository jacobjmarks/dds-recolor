namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Io;
using DdsRecolor.Core.Recolor;

/// <summary>
/// Tests for the detector that guards against a repeated <c>--in-place</c> run.
/// </summary>
/// <remarks>
/// The block engine applies a fixed hue rotation, so a second pass rotates
/// again. In-place writes are irreversible, so this warning is the only thing
/// standing between a user's re-run and progressively distorted textures.
/// </remarks>
public class RecolorDetectorTests
{
    private static readonly Rgb Brown = new(0x8B, 0x5A, 0x2B);
    private static readonly Rgb Grey = new(0xC0, 0xC0, 0xC0);

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>
    /// The hue-only transform these tests reason about.
    /// </summary>
    /// <remarks>
    /// Both channels are retained so the detector is judged on hue movement
    /// alone. The production default also adjusts saturation and brightness,
    /// which would move colours for reasons unrelated to the hue distance the
    /// detector measures.
    /// </remarks>
    private static HueShift Shift(Rgb source, Rgb target) =>
        new(new RecolorOptions
        {
            Source = source,
            Target = target,
            RetainSaturation = true,
            RetainBrightness = true,
        });

    [Fact]
    public void OriginalTexture_IsNotFlaggedAsAlreadyRecolored()
    {
        // An untouched brown texture must not trip the warning, or every
        // legitimate first run would prompt for confirmation.
        var file = DdsFile.Load(FixturePath("bc1_gradient_64x64.dds"));

        var analysis = RecolorDetector.Analyse(file, Brown, Grey);

        Assert.False(analysis.LooksAlreadyRecolored,
            $"Original texture wrongly flagged. {analysis.Describe()}");
    }

    [Fact]
    public void RecoloredTexture_IsFlaggedAsAlreadyRecolored()
    {
        // The case the warning exists for: a texture that has already been
        // shifted toward the target, where running again would rotate further.
        var file = DdsFile.Load(FixturePath("bc1_gradient_64x64.dds"));
        var result = file.Recolor(Shift(Brown, Grey));

        var recolored = DdsFile.Load(result.Data);
        var analysis = RecolorDetector.Analyse(recolored, Brown, Grey);

        Assert.True(analysis.LooksAlreadyRecolored,
            $"Recoloured texture not flagged. {analysis.Describe()}");

        // And the underlying numbers should show it plainly: closer to the
        // target hue than to the source hue.
        Assert.True(analysis.TargetHueDistance < analysis.SourceHueDistance,
            $"Target distance {analysis.TargetHueDistance:F1} should be less than " +
            $"source distance {analysis.SourceHueDistance:F1}.");
    }

    [Fact]
    public void RepeatedRecolor_KeepsRotatingHueAwayFromTheOriginal()
    {
        // Documents the exact hazard the warning protects against: repeated
        // application does not converge, it keeps rotating. If this ever starts
        // failing, the transform has become idempotent and the warning is no
        // longer needed.
        var file = DdsFile.Load(FixturePath("bc1_gradient_64x64.dds"));

        byte[] once = file.Recolor(Shift(Brown, Grey)).Data;
        byte[] twice = DdsFile.Load(once).Recolor(Shift(Brown, Grey)).Data;
        byte[] thrice = DdsFile.Load(twice).Recolor(Shift(Brown, Grey)).Data;

        Assert.NotEqual(Convert.ToHexString(once), Convert.ToHexString(twice));
        Assert.NotEqual(Convert.ToHexString(twice), Convert.ToHexString(thrice));

        // Size is still preserved on every pass, so the only visible symptom is
        // colour drift, which is exactly why a warning is needed.
        Assert.Equal(file.Data.Length, once.Length);
        Assert.Equal(file.Data.Length, twice.Length);
        Assert.Equal(file.Data.Length, thrice.Length);

        // Each pass should move the mean hue further from the source.
        double DistanceFromSource(byte[] data)
        {
            var a = RecolorDetector.Analyse(DdsFile.Load(data), Brown, Grey);
            return a.SourceHueDistance;
        }

        double d0 = DistanceFromSource(file.Data);
        double d1 = DistanceFromSource(once);
        double d2 = DistanceFromSource(twice);

        Assert.True(d1 > d0, $"First pass should increase distance ({d0:F1} -> {d1:F1}).");
        Assert.True(d2 > d1, $"Second pass should increase it again ({d1:F1} -> {d2:F1}).");
    }

    [Fact]
    public void FlatFixture_IsNeverFlagged()
    {
        // The flat fixture has equal endpoints and no saturated colour, so there
        // is nothing to judge. Reporting "already recolored" would be a guess,
        // and a wrong guess would nag the user on every run.
        var file = DdsFile.Load(FixturePath("bc1_flat_4x4.dds"));

        var analysis = RecolorDetector.Analyse(file, Brown, Grey);

        Assert.False(analysis.LooksAlreadyRecolored);
        Assert.Equal(0, analysis.SamplesAnalysed);
    }

    [Fact]
    public void Detector_IsNotFooledByATextureWhoseHueSitsBetweenSourceAndTarget()
    {
        // A texture whose colours are roughly equidistant from both hues must not
        // be flagged, or the warning fires on legitimate first runs and trains
        // the user to ignore it.
        //
        // Use a source and target only a few degrees apart, so every texture in
        // the fixture set is far from both and no clear margin exists.
        var file = DdsFile.Load(FixturePath("bc1_noisy_512x512.dds"));

        var analysis = RecolorDetector.Analyse(
            file, new Rgb(0x8B, 0x5A, 0x2B), new Rgb(0x8F, 0x5E, 0x2F));

        Assert.False(analysis.LooksAlreadyRecolored,
            $"Should not flag when source and target are nearly identical. {analysis.Describe()}");
    }

    [Theory]
    [InlineData("bc1_gradient_64x64.dds")]
    [InlineData("bc1_square_512x512.dds")]
    [InlineData("bc1_nonsquare_1024x512.dds")]
    [InlineData("bc1_noisy_512x512.dds")]
    [InlineData("bc3_nonsquare_512x256.dds")]
    public void RecoloredTexture_IsFlagged_AcrossFormatsAndShapes(string name)
    {
        // The detector walks the mip table and reads block endpoints, so it must
        // handle BC3's colour-block offset and non-square shapes correctly
        // rather than reading alpha bytes as colour.
        var file = DdsFile.Load(FixturePath(name));

        var original = RecolorDetector.Analyse(file, Brown, Grey);
        Assert.False(original.LooksAlreadyRecolored,
            $"Original {name} wrongly flagged. {original.Describe()}");

        byte[] recoloredBytes = file.Recolor(Shift(Brown, Grey)).Data;
        var recolored = RecolorDetector.Analyse(DdsFile.Load(recoloredBytes), Brown, Grey);

        Assert.True(recolored.LooksAlreadyRecolored,
            $"Recoloured {name} not flagged. {recolored.Describe()}");
    }

    [Fact]
    public void TextureWhoseHuesSitBeyondTheSource_IsDeliberatelyNotFlagged()
    {
        // Documents the known limitation rather than hiding it.
        //
        // This fixture's endpoints sit at hues 58-69 degrees, which is on the
        // FAR side of the source (29 degrees) from the target (0 degrees). The
        // rotation moves them to 29-40 degrees, i.e. back toward the source, so
        // a second run would partly UNDO the first rather than compound it.
        //
        // Flagging it would be a false positive that fires on legitimate runs and
        // trains the user to confirm without reading, so the detector stays
        // silent. This test pins that choice so a future change is noticed.
        var file = DdsFile.Load(FixturePath("bc1_lowcontrast_256x256.dds"));

        // The shift genuinely does modify this texture...
        var result = file.Recolor(Shift(Brown, Grey));
        Assert.True(result.BlocksModified > 0);
        Assert.False(file.Data.AsSpan().SequenceEqual(result.Data));

        // ...but the detector correctly declines to call it recoloured, because
        // the target hue is not the direction those colours moved.
        var analysis = RecolorDetector.Analyse(DdsFile.Load(result.Data), Brown, Grey);
        Assert.False(analysis.LooksAlreadyRecolored, analysis.Describe());
        Assert.True(analysis.TargetHueDistance > analysis.SourceHueDistance,
            "These colours should still be closer to the source than the target. " +
            analysis.Describe());
    }
}
