using DdsRecolor.Cli;
using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Recolor;

namespace DdsRecolor.Gui;

public static class CliPreviewArguments
{
    public static bool TryParse(
        string text, string? currentFolder, out RecolorOptions options,
        out string message, out bool isWarning)
    {
        options = null!;
        message = string.Empty;
        isWarning = false;

        if (!CliArgumentText.TrySplit(text, out string[] arguments, out message)) return false;
        if (arguments.Length == 0)
        {
            message = "Enter CLI options, including --source and --target (or --target-hue-offset).";
            return false;
        }

        if (IsCliExecutable(arguments[0]))
        {
            arguments = arguments[1..];
        }
        else if (arguments.Length > 1 && arguments[0].Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(arguments[1]).Equals("dds-recolor", StringComparison.OrdinalIgnoreCase))
        {
            arguments = arguments[2..];
        }

        bool includesFolder = arguments.Length > 0 && !arguments[0].StartsWith('-');
        if (!includesFolder)
        {
            string folder = currentFolder is not null && Directory.Exists(currentFolder)
                ? currentFolder
                : Environment.CurrentDirectory;
            arguments = [folder, .. arguments];
        }

        CliOptions parsed;
        try
        {
            parsed = ArgumentParser.Parse(arguments);
        }
        catch (CliUsageException ex)
        {
            message = ex.Message;
            return false;
        }

        if (parsed.ShowHelp)
        {
            message = "Help text isn't a recolor configuration.";
            return false;
        }
        if (parsed.Mode != RecolorMode.Block)
        {
            message = "The GUI preview only supports --mode block.";
            return false;
        }

        var ignored = new List<string>();
        if (includesFolder) ignored.Add("folder path (preview stays on the active DDS)");
        if (parsed.OutputDirectory is not null) ignored.Add("--out");
        if (parsed.InPlace) ignored.Add("--in-place");
        if (parsed.AssumeYes) ignored.Add("--yes");
        if (parsed.Filter is not null) ignored.Add("--filter");
        if (parsed.Jobs != Environment.ProcessorCount) ignored.Add("--jobs");
        if (parsed.Verbose) ignored.Add("--verbose");
        if (parsed.Preview) ignored.Add("--preview");
        if (parsed.PreviewCount != 5) ignored.Add("--preview-count");

        options = parsed.ToRecolorOptions();
        if (ignored.Count > 0)
        {
            message = $"Preview ignores: {string.Join(", ", ignored)}.";
            isWarning = true;
        }

        return true;
    }

    public static string Format(RecolorOptions options)
    {
        var arguments = new List<string>
        {
            "--source", $"'{options.Source}'", "--target", $"'{options.Target}'",
        };
        if (options.Intensity != 1.0) AddValue(arguments, "--intensity", options.Intensity);
        if (options.AlphaMode != AlphaMode.Straight) arguments.AddRange(["--alpha-mode", "premultiplied"]);
        if (options.HueToleranceDegrees != 60.0) AddValue(arguments, "--tolerance", options.HueToleranceDegrees);
        if (options.RetainSaturation) arguments.Add("--retain-saturation");
        if (options.SaturationStrength != 1.0) AddValue(arguments, "--saturation", options.SaturationStrength);
        if (options.RetainBrightness) arguments.Add("--retain-brightness");
        if (options.BrightnessStrength != 1.0) AddValue(arguments, "--brightness", options.BrightnessStrength);
        return string.Join(' ', arguments);
    }

    private static bool IsCliExecutable(string argument) =>
        Path.GetFileNameWithoutExtension(argument).Equals("dds-recolor", StringComparison.OrdinalIgnoreCase);

    private static void AddValue(List<string> arguments, string name, double value)
    {
        arguments.Add(name);
        arguments.Add(value.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
    }
}