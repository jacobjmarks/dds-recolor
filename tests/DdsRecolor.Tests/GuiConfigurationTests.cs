namespace DdsRecolor.Tests;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Recolor;
using DdsRecolor.Gui;

public sealed class GuiConfigurationTests
{
    [Fact]
    public void CliArgumentText_PreservesQuotedArgumentsAndWindowsPaths()
    {
        const string text = "\"D:\\texture packs\\wood\" --source '#8B5A2B' --target \"#D89D83\"";

        Assert.True(CliArgumentText.TrySplit(text, out string[] arguments, out string error), error);
        Assert.Equal(["D:\\texture packs\\wood", "--source", "#8B5A2B", "--target", "#D89D83"], arguments);
    }

    [Fact]
    public void CliArgumentText_ReportsUnclosedQuote()
    {
        Assert.False(CliArgumentText.TrySplit("--source '#123456", out _, out string error));
        Assert.Contains("closing quote", error);
    }

    [Fact]
    public void CliPreviewArguments_RoundTripsRecolorOptionsThroughCliParser()
    {
        string folder = Path.GetTempPath();
        const string text = "--source '#8B5A2B' --target '#D89D83' --intensity 0.75 " +
                            "--alpha-mode premultiplied --tolerance 360 --saturation 0.4 " +
                            "--brightness 0.6 --retain-brightness";

        Assert.True(CliPreviewArguments.TryParse(text, folder, out RecolorOptions parsed, out string message, out bool warning), message);
        Assert.False(warning);
        Assert.Equal(new Rgb(0x8B, 0x5A, 0x2B), parsed.Source);
        Assert.Equal(new Rgb(0xD8, 0x9D, 0x83), parsed.Target);
        Assert.Equal(0.75, parsed.Intensity);
        Assert.Equal(AlphaMode.Premultiplied, parsed.AlphaMode);
        Assert.Equal(360, parsed.HueToleranceDegrees);
        Assert.Equal(0.4, parsed.SaturationStrength);
        Assert.Equal(0.6, parsed.BrightnessStrength);
        Assert.True(parsed.RetainBrightness);

        string canonical = CliPreviewArguments.Format(parsed);
        Assert.True(CliPreviewArguments.TryParse(canonical, folder, out RecolorOptions roundTripped, out message, out warning), message);
        Assert.Equal(parsed.Source, roundTripped.Source);
        Assert.Equal(parsed.Target, roundTripped.Target);
        Assert.Equal(parsed.SaturationStrength, roundTripped.SaturationStrength);
        Assert.Equal(parsed.BrightnessStrength, roundTripped.BrightnessStrength);
        Assert.Equal(parsed.RetainBrightness, roundTripped.RetainBrightness);
    }

    [Fact]
    public void CliPreviewArguments_WarnsWhenPastedCliOptionsDoNotAffectPreview()
    {
        string folder = Path.GetTempPath();
        string text = $"'{folder}' --source #803333 --target tomato --filter '*wood*' --out output";

        Assert.True(CliPreviewArguments.TryParse(text, folder, out _, out string message, out bool warning), message);
        Assert.True(warning);
        Assert.Contains("--filter", message);
        Assert.Contains("--out", message);
        Assert.Contains("active DDS", message);
    }

    [Fact]
    public void CliPreviewArguments_ReportsUnknownArguments()
    {
        Assert.False(CliPreviewArguments.TryParse("--source red --target blue --typo 2",
            Path.GetTempPath(), out _, out string message, out _));
        Assert.Contains("Unknown option '--typo'", message);
    }

    [Fact]
    public void GuiSessionSettings_RoundTripsLastLocationAndArguments()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dds-recolor-{Guid.NewGuid():N}.json");
        var settings = new GuiSessionSettings
        {
            OpenPath = Path.GetTempPath(),
            OpenedAsSingleFile = false,
            ActiveFilePath = Path.Combine(Path.GetTempPath(), "texture.dds"),
            LastValidArguments = "--source '#803333' --target tomato",
        };

        try
        {
            settings.Save(path);
            GuiSessionSettings loaded = GuiSessionSettings.Load(path);
            Assert.Equal(settings.OpenPath, loaded.OpenPath);
            Assert.Equal(settings.OpenedAsSingleFile, loaded.OpenedAsSingleFile);
            Assert.Equal(settings.ActiveFilePath, loaded.ActiveFilePath);
            Assert.Equal(settings.LastValidArguments, loaded.LastValidArguments);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}