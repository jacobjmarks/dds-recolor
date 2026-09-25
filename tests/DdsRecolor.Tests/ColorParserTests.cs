namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;

/// <summary>
/// Tests for the colour input syntaxes.
/// </summary>
/// <remarks>
/// These matter more than they look: a colour string is the one place a user's
/// intent is expressed, and a mis-parse produces a plausible-looking but wrong
/// recolour across an entire texture set. A silently clamped or misread channel
/// would be far harder to notice than an outright error, so the rejection cases
/// below are as important as the acceptance ones.
/// </remarks>
public class ColorParserTests
{
    // -----------------------------------------------------------------------
    // Hex
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("#8B5A2B", 0x8B, 0x5A, 0x2B)]
    [InlineData("#8b5a2b", 0x8B, 0x5A, 0x2B)]
    [InlineData("8B5A2B", 0x8B, 0x5A, 0x2B)]
    [InlineData("0x8B5A2B", 0x8B, 0x5A, 0x2B)]
    [InlineData("#FA0", 0xFF, 0xAA, 0x00)]
    [InlineData("#FFF", 0xFF, 0xFF, 0xFF)]
    [InlineData("#000", 0x00, 0x00, 0x00)]
    [InlineData("#8B5A2BFF", 0x8B, 0x5A, 0x2B)]
    [InlineData("  #8B5A2B  ", 0x8B, 0x5A, 0x2B)]
    public void Hex_IsParsed(string input, int r, int g, int b)
    {
        Assert.True(ColorParser.TryParse(input, out Rgb colour));
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), colour);
    }

    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#8B5A2B00")]
    [InlineData("#8B5A2B80")]
    public void InvalidHex_IsRejected(string input)
    {
        Assert.False(ColorParser.TryParse(input, out Rgb _));
    }

    // -----------------------------------------------------------------------
    // Named colours
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("tomato", 0xFF, 0x63, 0x47)]
    [InlineData("TOMATO", 0xFF, 0x63, 0x47)]
    [InlineData("Tomato", 0xFF, 0x63, 0x47)]
    [InlineData("sienna", 0xA0, 0x52, 0x2D)]
    [InlineData("rebeccapurple", 0x66, 0x33, 0x99)]
    [InlineData("white", 0xFF, 0xFF, 0xFF)]
    [InlineData("black", 0x00, 0x00, 0x00)]
    public void Named_IsParsed(string input, int r, int g, int b)
    {
        Assert.True(ColorParser.TryParse(input, out Rgb colour));
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), colour);
    }

    [Fact]
    public void NamedTable_IsTheCompleteCssSet()
    {
        // A partial table would leave the user unable to tell an unsupported
        // name from a misspelled one, so the full CSS set is expected.
        Assert.Equal(148, NamedColors.Count);
    }

    [Fact]
    public void NamedLookup_HandlesBothGreySpellings()
    {
        Assert.True(NamedColors.TryGet("gray", out Rgb gray));
        Assert.True(NamedColors.TryGet("grey", out Rgb grey));
        Assert.Equal(gray, grey);
    }

    // -----------------------------------------------------------------------
    // rgb()
    // -----------------------------------------------------------------------

    [Fact]
    public void Rgb_AcceptsCommaSpaceAndBareForms()
    {
        var expected = new Rgb(0x8B, 0x5A, 0x2B);

        Assert.True(ColorParser.TryParse("rgb(139, 90, 43)", out Rgb comma));
        Assert.True(ColorParser.TryParse("rgb(139 90 43)", out Rgb space));
        Assert.True(ColorParser.TryParse("rgb 139 90 43", out Rgb bare));

        Assert.Equal(expected, comma);
        Assert.Equal(expected, space);
        Assert.Equal(expected, bare);
    }

    [Fact]
    public void Rgb_DetectsZeroToOneRange()
    {
        Assert.True(ColorParser.TryParse("rgb(0 0 0)", out Rgb black));
        Assert.Equal(new Rgb(0, 0, 0), black);

        Assert.True(ColorParser.TryParse("rgb(1 1 1)", out Rgb white));
        Assert.Equal(new Rgb(255, 255, 255), white);

        Assert.True(ColorParser.TryParse("rgb(255 255 255)", out Rgb alsoWhite));
        Assert.Equal(new Rgb(255, 255, 255), alsoWhite);
    }

    [Fact]
    public void Rgb_RangeIsDecidedPerTripleNotPerComponent()
    {
        // A mixed triple must not be read component-by-component. With 128 in
        // play all three are 0-255, so the leading 1 means 1/255, not full.
        Assert.True(ColorParser.TryParse("rgb(1, 128, 0)", out Rgb mixed));

        Assert.Equal(1, mixed.R);
        Assert.Equal(128, mixed.G);
        Assert.Equal(0, mixed.B);
    }

    [Fact]
    public void Rgb_AcceptsPercentages()
    {
        Assert.True(ColorParser.TryParse("rgb(100% 0% 0%)", out Rgb red));
        Assert.Equal(new Rgb(255, 0, 0), red);

        Assert.True(ColorParser.TryParse("rgb(50% 50% 50%)", out Rgb mid));
        Assert.Equal(128, mid.R);
    }

    [Theory]
    [InlineData("rgb(256, 0, 0)")]
    [InlineData("rgb(300, 0, 0)")]
    [InlineData("rgb(-1, 0, 0)")]
    [InlineData("rgb(101%, 0%, 0%)")]
    [InlineData("rgb(1.5 0 0)")]
    [InlineData("rgb(0, 0)")]
    [InlineData("rgb(0, 0, 0, 0, 0)")]
    public void Rgb_OutOfRangeIsRejected(string input)
    {
        // Rejected rather than clamped: a clamped value would produce a
        // plausible but wrong colour, which is harder to spot than an error.
        Assert.False(ColorParser.TryParse(input, out Rgb _));
    }

    [Fact]
    public void Alpha_MustBeFullyOpaque()
    {
        // The engine shifts colour and has no opacity channel to write, so a
        // non-opaque alpha is a request it cannot honour.
        Assert.True(ColorParser.TryParse("rgb(139 90 43 / 1)", out Rgb _));
        Assert.True(ColorParser.TryParse("rgb(139 90 43 / 100%)", out Rgb _));

        Assert.False(ColorParser.TryParse("rgb(139 90 43 / 0.5)", out Rgb _));
        Assert.False(ColorParser.TryParse("rgb(139 90 43 / 50%)", out Rgb _));
    }
}

