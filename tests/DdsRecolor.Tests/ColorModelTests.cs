namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;

/// <summary>
/// Tests for the HSL, HSV, HWB and Lab syntaxes, plus round-trip fidelity.
/// </summary>
public class ColorModelTests
{
    // -----------------------------------------------------------------------
    // hsl()
    // -----------------------------------------------------------------------

    [Fact]
    public void Hsl_AcceptsCommaSpaceAndUnitForms()
    {
        Assert.True(ColorParser.TryParse("hsl(29, 53%, 36%)", out Rgb comma));
        Assert.True(ColorParser.TryParse("hsl(29 53% 36%)", out Rgb space));
        Assert.True(ColorParser.TryParse("hsl(29deg 53% 36%)", out Rgb deg));
        Assert.True(ColorParser.TryParse("hsl(29 0.53 0.36)", out Rgb fraction));

        Assert.Equal(comma, space);
        Assert.Equal(comma, deg);

        // The fraction form rounds to within one unit per channel.
        Assert.InRange(fraction.R, comma.R - 1, comma.R + 1);
        Assert.InRange(fraction.G, comma.G - 1, comma.G + 1);
        Assert.InRange(fraction.B, comma.B - 1, comma.B + 1);
    }

    [Theory]
    [InlineData("hsl(0 100% 50%)", 0xFF, 0x00, 0x00)]
    [InlineData("hsl(120 100% 50%)", 0x00, 0xFF, 0x00)]
    [InlineData("hsl(240 100% 50%)", 0x00, 0x00, 0xFF)]
    [InlineData("hsl(0 0% 100%)", 0xFF, 0xFF, 0xFF)]
    [InlineData("hsl(0 0% 0%)", 0x00, 0x00, 0x00)]
    public void Hsl_PrimariesAreCorrect(string input, int r, int g, int b)
    {
        Assert.True(ColorParser.TryParse(input, out Rgb colour));
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), colour);
    }

    [Fact]
    public void Hue_AcceptsAllCssUnits()
    {
        // 180 degrees expressed four ways must give the same colour.
        Assert.True(ColorParser.TryParse("hsl(180 100% 50%)", out Rgb degrees));
        Assert.True(ColorParser.TryParse("hsl(180deg 100% 50%)", out Rgb deg));
        Assert.True(ColorParser.TryParse("hsl(200grad 100% 50%)", out Rgb grad));
        Assert.True(ColorParser.TryParse("hsl(0.5turn 100% 50%)", out Rgb turn));

        Assert.Equal(degrees, deg);
        Assert.Equal(degrees, grad);
        Assert.Equal(degrees, turn);

        // Radians need a tolerance because pi cannot be written exactly.
        string radians = Math.PI.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(ColorParser.TryParse($"hsl({radians}rad 100% 50%)", out Rgb rad));
        Assert.InRange(rad.R, degrees.R - 1, degrees.R + 1);
        Assert.InRange(rad.B, degrees.B - 1, degrees.B + 1);
    }

    [Fact]
    public void Hue_WrapsRatherThanErroring()
    {
        // Hue is cyclic, so an out-of-range value is meaningful, unlike an
        // out-of-range saturation.
        Assert.True(ColorParser.TryParse("hsl(400 100% 50%)", out Rgb over));
        Assert.True(ColorParser.TryParse("hsl(40 100% 50%)", out Rgb normal));
        Assert.Equal(normal, over);

        Assert.True(ColorParser.TryParse("hsl(-30 100% 50%)", out Rgb negative));
        Assert.True(ColorParser.TryParse("hsl(330 100% 50%)", out Rgb positive));
        Assert.Equal(positive, negative);
    }

    // -----------------------------------------------------------------------
    // hsv() and hwb()
    // -----------------------------------------------------------------------

    [Fact]
    public void Hsv_IsParsed()
    {
        Assert.True(ColorParser.TryParse("hsv(0 100% 100%)", out Rgb red));
        Assert.Equal(new Rgb(255, 0, 0), red);

        Assert.True(ColorParser.TryParse("hsv(0 0% 0%)", out Rgb black));
        Assert.Equal(new Rgb(0, 0, 0), black);
    }

    [Fact]
    public void Hwb_IsParsed()
    {
        Assert.True(ColorParser.TryParse("hwb(0 0% 0%)", out Rgb red));
        Assert.Equal(new Rgb(255, 0, 0), red);

        // Pure white: full whiteness, no blackness.
        Assert.True(ColorParser.TryParse("hwb(0 100% 0%)", out Rgb white));
        Assert.Equal(new Rgb(255, 255, 255), white);

        // Pure black: full blackness.
        Assert.True(ColorParser.TryParse("hwb(0 0% 100%)", out Rgb black));
        Assert.Equal(new Rgb(0, 0, 0), black);
    }

    [Fact]
    public void Hwb_ScalesDownWhenWhitenessAndBlacknessExceedOne()
    {
        // CSS scales proportionally rather than treating it as invalid, and a
        // mid-grey is the expected result.
        Assert.True(ColorParser.TryParse("hwb(0 100% 100%)", out Rgb grey));

        Assert.Equal(grey.R, grey.G);
        Assert.Equal(grey.G, grey.B);
    }

    [Fact]
    public void Hwb_DescribesAPaleColourMoreDirectlyThanHsl()
    {
        // The reason HWB is offered: "a pale version of red" is one step here.
        Assert.True(ColorParser.TryParse("hwb(0 50% 0%)", out Rgb paleRed));

        var hsv = Hsv.FromRgb(paleRed);
        Assert.Equal(0.0, hsv.H, 1);           // still red
        Assert.Equal(0.5, hsv.S, 2);           // half washed out
        Assert.Equal(1.0, hsv.V, 2);           // still full brightness
    }

    // -----------------------------------------------------------------------
    // lab()
    // -----------------------------------------------------------------------

    [Fact]
    public void Lab_LightnessEndpointsAreCorrect()
    {
        Assert.True(ColorParser.TryParse("lab(0 0 0)", out Rgb black));
        Assert.Equal(new Rgb(0, 0, 0), black);

        Assert.True(ColorParser.TryParse("lab(100 0 0)", out Rgb white));
        Assert.Equal(new Rgb(255, 255, 255), white);
    }

    [Fact]
    public void Lab_MidGreyHasEqualChannels()
    {
        Assert.True(ColorParser.TryParse("lab(53.6 0 0)", out Rgb mid));

        Assert.InRange(mid.R, 120, 140);
        Assert.Equal(mid.R, mid.G);
        Assert.Equal(mid.G, mid.B);
    }

    [Fact]
    public void Lab_OppositeChromaAxesGiveOppositeHues()
    {
        // The a axis runs green to red, the b axis blue to yellow.
        Assert.True(ColorParser.TryParse("lab(60 -50 0)", out Rgb greenish));
        Assert.True(ColorParser.TryParse("lab(60 50 0)", out Rgb reddish));

        Assert.True(greenish.G > greenish.R, "Negative a should be green-dominant.");
        Assert.True(reddish.R > reddish.G, "Positive a should be red-dominant.");

        Assert.True(ColorParser.TryParse("lab(70 0 -50)", out Rgb bluish));
        Assert.True(ColorParser.TryParse("lab(70 0 50)", out Rgb yellowish));

        Assert.True(bluish.B > bluish.R, "Negative b should be blue-dominant.");
        Assert.True(yellowish.R > yellowish.B, "Positive b should be yellow-dominant.");
    }

    [Fact]
    public void Lab_LightnessAcceptsPercentage()
    {
        Assert.True(ColorParser.TryParse("lab(100 0 0)", out Rgb bare));
        Assert.True(ColorParser.TryParse("lab(100% 0 0)", out Rgb percent));
        Assert.Equal(bare, percent);
    }

    // -----------------------------------------------------------------------
    // Rejections for the modern formats
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("hsl(29 53 36)")]       // bare values above 1 are ambiguous
    [InlineData("hsl(29deg 150% 36%)")]  // saturation out of range
    [InlineData("hsl(29 53% 150%)")]     // lightness out of range
    [InlineData("hwb(0 150% 0%)")]       // whiteness over 100%
    [InlineData("lab(150 0 0)")]         // lightness out of range
    [InlineData("lab(50 500 0)")]        // chroma implausible
    [InlineData("rgb()")]                // empty
    [InlineData("lab()")]
    [InlineData("hsl()")]
    public void InvalidComponents_AreRejected(string input)
    {
        Assert.False(ColorParser.TryParse(input, out Rgb _));
    }

    [Fact]
    public void NegativeHue_WrapsRatherThanErroring()
    {
        // Hue is cyclic, so a negative value is meaningful and wraps, matching
        // CSS. This is deliberately NOT rejected: only non-hue components have
        // a hard range.
        Assert.True(ColorParser.TryParse("hwb(-1 0% 0%)", out Rgb fromNegative));
        Assert.True(ColorParser.TryParse("hwb(359 0% 0%)", out Rgb fromPositive));

        Assert.InRange(fromNegative.R, fromPositive.R - 1, fromPositive.R + 1);
        Assert.InRange(fromNegative.B, fromPositive.B - 1, fromPositive.B + 1);
    }

    [Fact]
    public void BareComponentAboveOneIsNotSilentlyTreatedAsPercent()
    {
        // "hsl(29 53 36)" is a common typo for "hsl(29 53% 36%)". Reading it as
        // 53% and 36% would give a plausible colour and hide the mistake, so it
        // is rejected instead.
        Assert.False(ColorParser.TryParse("hsl(29 53 36)", out Rgb _));
        Assert.True(ColorParser.TryParse("hsl(29 0.53 0.36)", out Rgb _));
    }

    // -----------------------------------------------------------------------
    // Format reporting
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("#8B5A2B", ColorFormat.Hex)]
    [InlineData("tomato", ColorFormat.Named)]
    [InlineData("rgb(139 90 43)", ColorFormat.Rgb)]
    [InlineData("hsl(29 53% 36%)", ColorFormat.Hsl)]
    [InlineData("hsv(29 53% 55%)", ColorFormat.Hsv)]
    [InlineData("hwb(29 17% 45%)", ColorFormat.Hwb)]
    [InlineData("lab(45 18 32)", ColorFormat.Lab)]
    public void Format_IsReportedCorrectly(string input, ColorFormat expected)
    {
        Assert.True(ColorParser.TryParse(input, out ParsedColor parsed));
        Assert.Equal(expected, parsed.Format);
    }

    // -----------------------------------------------------------------------
    // Round trips
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0x8B, 0x5A, 0x2B)]
    [InlineData(0xD8, 0x9D, 0x83)]
    [InlineData(0x80, 0x33, 0x33)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x7F, 0x7F, 0x7F)]
    [InlineData(0x00, 0xFF, 0x00)]
    [InlineData(0xFF, 0x00, 0xFF)]
    public void EveryModel_RoundTripsExactly(int r, int g, int b)
    {
        // A round trip through each model must return the original colour, or a
        // user could express the same colour two ways and get two different
        // results. Exactness is achievable for these models at 8-bit precision.
        var original = new Rgb((byte)r, (byte)g, (byte)b);

        Assert.Equal(original, Hsl.FromRgb(original).ToRgb());
        Assert.Equal(original, Hsv.FromRgb(original).ToRgb());
        Assert.Equal(original, Hwb.FromRgb(original).ToRgb());
        Assert.Equal(original, Lab.FromRgb(original).ToRgb());
    }

    [Fact]
    public void Lab_RoundTripsAcrossTheWholeGamut()
    {
        // Sample the cube rather than a few corners: a conversion error usually
        // shows up mid-range, where the gamma curve is steepest.
        for (int r = 0; r <= 255; r += 17)
        {
            for (int g = 0; g <= 255; g += 17)
            {
                for (int b = 0; b <= 255; b += 17)
                {
                    var original = new Rgb((byte)r, (byte)g, (byte)b);

                    // Lab is a floating-point conversion, so allow one unit of
                    // quantisation slack per channel.
                    var round = Lab.FromRgb(original).ToRgb();
                    Assert.True(Math.Abs(round.R - original.R) <= 1,
                        $"R drifted for {original}: {original} -> {round}");
                    Assert.True(Math.Abs(round.G - original.G) <= 1,
                        $"G drifted for {original}: {original} -> {round}");
                    Assert.True(Math.Abs(round.B - original.B) <= 1,
                        $"B drifted for {original}: {original} -> {round}");
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Typo suggestions
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("tomatoe", "tomato")]
    [InlineData("siena", "sienna")]
    [InlineData("rebeccapurpl", "rebeccapurple")]
    [InlineData("graey", "gray")]
    public void Typos_SuggestTheIntendedName(string typo, string expected)
    {
        var suggestions = NameSuggester.Suggest(typo);
        Assert.Contains(expected, suggestions);
    }

    [Fact]
    public void Suggestions_AreNotOfferedForNonNames()
    {
        // Hex strings and nonsense must not attract suggestions, or the error
        // message becomes noise that trains the user to ignore it.
        Assert.Empty(NameSuggester.Suggest("#8B5A2B"));
        Assert.Empty(NameSuggester.Suggest("rgb(1 2 3)"));
        Assert.Empty(NameSuggester.Suggest("xyzzy"));
        Assert.Empty(NameSuggester.Suggest("ab"));
    }

    [Fact]
    public void ParseError_MentionsTheSupportedFormats()
    {
        var ex = Assert.Throws<FormatException>(() => ColorParser.Parse("nonsense"));
        Assert.Contains("Expected one of", ex.Message);
        Assert.Contains("tomato", ex.Message);
    }

    [Fact]
    public void ParseError_IncludesATypoSuggestion()
    {
        var ex = Assert.Throws<FormatException>(() => ColorParser.Parse("tomatoe"));
        Assert.Contains("Did you mean", ex.Message);
        Assert.Contains("tomato", ex.Message);
    }
}


