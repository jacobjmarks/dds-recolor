namespace DdsRecolor.Cli;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Recolor;

/// <summary>Which engine to use when recoloring.</summary>
public enum RecolorMode
{
    /// <summary>Rewrite block endpoints in place. Exact size, deterministic.</summary>
    Block,

    /// <summary>Decode, recolor, re-encode. Not implemented yet.</summary>
    Reencode,

    /// <summary>Produce both, into separate output trees.</summary>
    Both,
}

/// <summary>Parsed command-line options.</summary>
public sealed class CliOptions
{
    public required string Folder { get; init; }
    public required Rgb Source { get; init; }
    public required Rgb Target { get; init; }

    /// <summary>
    /// Alternative to <see cref="Target"/>: rotate the source hue by this many
    /// degrees instead of naming a colour.
    /// </summary>
    /// <remarks>
    /// Convenient when the goal is a relative change rather than a specific
    /// colour, for example "shift everything 30 degrees warmer" without having to
    /// work out what colour that lands on. Mutually exclusive with
    /// <c>--target</c> and <c>--target-hue-offset</c>, resolved at parse time.
    /// </remarks>
    public double? TargetHueOffset { get; init; }

    public double Intensity { get; init; } = 1.0;
    public RecolorMode Mode { get; init; } = RecolorMode.Block;
    public string? OutputDirectory { get; init; }
    public bool InPlace { get; init; }

    /// <summary>
    /// Skip the interactive confirmation that guards <c>--in-place</c>.
    /// </summary>
    /// <remarks>
    /// Provided so scripted or repeated runs are not blocked by a prompt, while
    /// an interactive user still gets the safety check. Use is deliberate: it
    /// only has an effect together with <c>--in-place</c>.
    /// </remarks>
    public bool AssumeYes { get; init; }

    public bool Preview { get; init; }
    public int PreviewCount { get; init; } = 5;
    public AlphaMode AlphaMode { get; init; } = AlphaMode.Straight;
    public double Tolerance { get; init; } = 60.0;

    /// <summary>
    /// Keep saturation from the source image rather than adjusting it toward the
    /// target's.
    /// </summary>
    public bool RetainSaturation { get; init; }

    /// <summary>How far to move saturation toward the target's, in [0,1]. Ignored when retained.</summary>
    public double SaturationStrength { get; init; } = 1.0;

    /// <summary>
    /// Keep brightness from the source image rather than adjusting it toward the
    /// target's.
    /// </summary>
    public bool RetainBrightness { get; init; }

    /// <summary>How far to move brightness toward the target's, in [0,1]. Ignored when retained.</summary>
    public double BrightnessStrength { get; init; } = 1.0;
    public string? Filter { get; init; }
    public int Jobs { get; init; } = Environment.ProcessorCount;
    public bool Verbose { get; init; }
    public bool ShowHelp { get; init; }

    /// <summary>Resolve the output root, honouring <c>--out</c> and <c>--in-place</c>.</summary>
    public string ResolveOutputRoot()
    {
        if (OutputDirectory is not null) return Path.GetFullPath(OutputDirectory);

        string trimmed = Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFullPath(trimmed + "_recolored");
    }

    /// <summary>Build the recolor options the core library consumes.</summary>
    public RecolorOptions ToRecolorOptions() => new()
    {
        Source = Source,
        Target = Target,
        Intensity = Intensity,
        AlphaMode = AlphaMode,
        HueToleranceDegrees = Tolerance,
        RetainSaturation = RetainSaturation,
        SaturationStrength = SaturationStrength,
        RetainBrightness = RetainBrightness,
        BrightnessStrength = BrightnessStrength,
    };
}

/// <summary>Raised for a user-facing argument error, as opposed to a crash.</summary>
public sealed class CliUsageException(string message) : Exception(message);

/// <summary>Parses <see cref="CliOptions"/> from raw command-line arguments.</summary>
public static class ArgumentParser
{
    public const string Usage = """
        dds-recolor - recolor DDS textures by mapping a source colour to a target colour

        USAGE
          dds-recolor <folder> --source <#rrggbb> --target <#rrggbb> [options]

        REQUIRED
          <folder>                Folder containing .dds files. Searched recursively.
          --source <colour>       Colour to remap.
          --target <colour>       Colour to remap toward. Required unless
                                  --target-hue-offset is given.

        COLOUR FORMATS (for both --source and --target)
          #8B5A2B #8b5a2b 8B5A2B 0x8B5A2B   hex, with # or 0x optional
          #FA0                             3-digit short form
          #8B5A2BFF                        with opaque alpha (alpha must be FF)
          tomato sienna rebeccapurple      any CSS colour name
          rgb(139, 90, 43)                 rgb, 0-255 or 0-1 or percentages
          rgb(0.55 0.35 0.17)              space-separated form also accepted
          hsl(29 53% 36%)                  hsl, with optional deg/grad/rad/turn
          hsv(29 53% 55%)                  hsv
          hwb(29 17% 45%)                  hwb (whiteness and blackness)
          lab(45 18 32)                    CIELAB, lightness as 0-100 or a percentage
          rgb 139 90 43                    the parentheses are optional

        OUTPUT
          --out <dir>             Output root. Default: {folder}_recolored
          --in-place              Overwrite the originals atomically instead of writing alongside.
                                  Prompts for confirmation; pass --yes to skip the prompt.
          --yes, -y               Skip the --in-place confirmation prompt (for scripts).
          --mode <block|reencode|both>
                                  Recolor technique. Default: block
                                    block    rewrite block colour endpoints; exact file size,
                                             deterministic, no new block artifacts
                                    reencode decode, recolor, re-encode the whole texture
                                    both     write both, into separate output subfolders
          --preview               Dry run: write before/after PNGs for a sample, no .dds output.
          --preview-count <n>     How many files to preview. Default: 5

        COLOUR
          --target-hue-offset <deg>
                                  Instead of naming a target colour, rotate the
                                  source hue by this many degrees. Negative is
                                  allowed. e.g. 30 warms, -30 cools.
          --intensity <n>         Strength of the hue shift. Default: 1.0
                                  0 leaves the texture byte-identical (useful as a check).

                                  BY DEFAULT all three channels are adjusted so the
                                  source colour matches the target. Source and target
                                  often differ more in saturation and brightness than
                                  in hue, and adjusting hue alone leaves a dark
                                  saturated colour looking unchanged. Add the flags
                                  below to keep a channel as-is instead.

          --retain-saturation     Keep saturation from the source image. Useful when
                                  the texture's vividness should not change.
          --retain-brightness     Keep brightness from the source image. Useful when
                                  brightness carries lighting and geometry detail
                                  that must not be disturbed.
          --saturation <0..1>     How far to adjust saturation. Default: 1.0
          --brightness <0..1>     How far to adjust brightness. Default: 1.0
          --alpha-mode <straight|premultiplied>
                                  How alpha is encoded. Default: straight
          --tolerance <0..360>    Hue window around the source colour, in degrees. Default: 60

        FILTERING
          --filter <glob>         Only process files whose name matches, e.g. "*wood*"

        OTHER
          --jobs <n>              Parallel workers. Default: processor count
          --verbose               Per-file detail
          -h, --help              Show this help

        EXAMPLES
          dds-recolor "D:\textures" --source #803333 --target #D89D83
          dds-recolor "D:\textures" --source #803333 --target #D89D83 --retain-brightness
          dds-recolor "D:\textures" --source #803333 --target tomato
          dds-recolor "D:\textures" --source "hsl(0 60% 35%)" --target "lab(70 -10 25)"
          dds-recolor "D:\textures" --source #803333 --target-hue-offset 30
          dds-recolor "D:\textures" --source #8B5A2B --target #C0C0C0 --preview
          dds-recolor "D:\textures" --source #8B5A2B --target #C0C0C0 --in-place --yes

        EXIT CODES
          0  success
          1  one or more files failed
          2  invalid arguments
          3  requested mode not implemented
          4  aborted at the in-place confirmation prompt
        """;

    /// <summary>Parse arguments.</summary>
    /// <exception cref="CliUsageException">An argument is missing or malformed.</exception>
    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new CliUsageException("No arguments supplied.");
        }

        if (args.Any(a => a is "-h" or "--help" or "/?"))
        {
            return new CliOptions
            {
                Folder = string.Empty,
                Source = default,
                Target = default,
                ShowHelp = true,
            };
        }

        string? folder = null;
        Rgb? source = null;
        Rgb? target = null;

        double intensity = 1.0;
        var mode = RecolorMode.Block;
        string? outputDirectory = null;
        bool inPlace = false;
        bool assumeYes = false;
        bool preview = false;
        int previewCount = 5;
        var alphaMode = AlphaMode.Straight;
        double tolerance = 60.0;
        string? filter = null;
        int jobs = Environment.ProcessorCount;
        bool verbose = false;
        bool retainSaturation = false;
        double saturationStrength = 1.0;
        bool retainBrightness = false;
        double brightnessStrength = 1.0;
        double? targetHueOffset = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (!arg.StartsWith('-'))
            {
                if (folder is not null)
                {
                    throw new CliUsageException(
                        $"Unexpected extra argument '{arg}'. Only one folder may be given.");
                }
                folder = arg;
                continue;
            }

            switch (arg)
            {
                case "--source":
                    try
                    {
                        source = ColorParser.Parse(Next(args, ref i, arg));
                    }
                    catch (FormatException ex)
                    {
                        throw new CliUsageException(ex.Message);
                    }
                    break;

                case "--target":
                    try
                    {
                        target = ColorParser.Parse(Next(args, ref i, arg));
                    }
                    catch (FormatException ex)
                    {
                        throw new CliUsageException(ex.Message);
                    }
                    break;

                case "--target-hue-offset":
                    targetHueOffset = ParseDouble(Next(args, ref i, arg), arg);
                    if (targetHueOffset < -360.0 || targetHueOffset > 360.0)
                    {
                        throw new CliUsageException(
                            "--target-hue-offset must be between -360 and 360 degrees.");
                    }
                    break;

                case "--intensity":
                    intensity = ParseDouble(Next(args, ref i, arg), arg);
                    if (intensity < 0.0)
                    {
                        throw new CliUsageException("--intensity must be zero or greater.");
                    }
                    break;

                case "--mode":
                    mode = Next(args, ref i, arg).ToLowerInvariant() switch
                    {
                        "block" => RecolorMode.Block,
                        "reencode" => RecolorMode.Reencode,
                        "both" => RecolorMode.Both,
                        var other => throw new CliUsageException(
                            $"--mode must be block, reencode or both, not '{other}'."),
                    };
                    break;

                case "--out":
                    outputDirectory = Next(args, ref i, arg);
                    break;

                case "--in-place":
                    inPlace = true;
                    break;

                case "--yes":
                case "-y":
                    assumeYes = true;
                    break;

                case "--preview":
                    preview = true;
                    break;

                case "--preview-count":
                    previewCount = ParseInt(Next(args, ref i, arg), arg);
                    if (previewCount < 1)
                    {
                        throw new CliUsageException("--preview-count must be at least 1.");
                    }
                    break;

                case "--alpha-mode":
                    alphaMode = Next(args, ref i, arg).ToLowerInvariant() switch
                    {
                        "straight" => AlphaMode.Straight,
                        "premultiplied" => AlphaMode.Premultiplied,
                        var other => throw new CliUsageException(
                            $"--alpha-mode must be straight or premultiplied, not '{other}'."),
                    };
                    break;

                case "--retain-saturation":
                    retainSaturation = true;
                    break;

                case "--retain-brightness":
                    retainBrightness = true;
                    break;

                case "--saturation":
                    saturationStrength = ParseDouble(Next(args, ref i, arg), arg);
                    if (saturationStrength < 0.0 || saturationStrength > 1.0)
                    {
                        throw new CliUsageException("--saturation must be between 0 and 1.");
                    }
                    break;

                case "--brightness":
                    brightnessStrength = ParseDouble(Next(args, ref i, arg), arg);
                    if (brightnessStrength < 0.0 || brightnessStrength > 1.0)
                    {
                        throw new CliUsageException("--brightness must be between 0 and 1.");
                    }
                    break;

                case "--tolerance":
                    tolerance = ParseDouble(Next(args, ref i, arg), arg);
                    if (tolerance < 0.0 || tolerance > 360.0)
                    {
                        throw new CliUsageException("--tolerance must be between 0 and 360.");
                    }
                    break;

                case "--filter":
                    filter = Next(args, ref i, arg);
                    break;

                case "--jobs":
                    jobs = ParseInt(Next(args, ref i, arg), arg);
                    if (jobs < 1)
                    {
                        throw new CliUsageException("--jobs must be at least 1.");
                    }
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                default:
                    throw new CliUsageException($"Unknown option '{arg}'.");
            }
        }

        // Validate in the order a user would want to hear about problems:
        //   1. structural argument errors (missing, conflicting, malformed),
        //   2. then anything that depends on the filesystem.
        // Doing the folder check first masks every other mistake behind
        // "folder does not exist", which is the least useful message when the
        // real problem is a typo in an option.
        if (folder is null)
        {
            throw new CliUsageException("No folder supplied.");
        }
        if (source is null)
        {
            throw new CliUsageException("--source is required.");
        }
        if (target is null && targetHueOffset is null)
        {
            throw new CliUsageException(
                "A target is required: pass --target <colour> or --target-hue-offset <degrees>.");
        }
        if (target is not null && targetHueOffset is not null)
        {
            throw new CliUsageException(
                "--target and --target-hue-offset cannot be combined; pick one.");
        }
        if (inPlace && outputDirectory is not null)
        {
            throw new CliUsageException("--in-place and --out cannot be combined.");
        }
        if (inPlace && preview)
        {
            throw new CliUsageException("--in-place and --preview cannot be combined.");
        }

        if (!Directory.Exists(folder))
        {
            throw new CliUsageException(
                $"Folder does not exist: {Path.GetFullPath(folder)}");
        }

        // A hue offset is resolved into a concrete target colour here, so the
        // rest of the pipeline sees a normal source/target pair and nothing
        // downstream has to know the offset form exists.
        //
        // The synthesised target keeps the source's saturation and value, which
        // makes the offset a pure hue rotation. That is the intuitive reading:
        // --target-hue-offset 30 means "30 degrees warmer", not "also change how
        // vivid and how bright the colour is".
        Rgb resolvedTarget = target ?? RotateHue(source.Value, targetHueOffset!.Value);

        return new CliOptions
        {
            Folder = Path.GetFullPath(folder),
            Source = source.Value,
            Target = resolvedTarget,
            TargetHueOffset = targetHueOffset,
            Intensity = intensity,
            Mode = mode,
            OutputDirectory = outputDirectory is null ? null : Path.GetFullPath(outputDirectory),
            InPlace = inPlace,
            AssumeYes = assumeYes,
            Preview = preview,
            PreviewCount = previewCount,
            AlphaMode = alphaMode,
            Tolerance = tolerance,
            RetainSaturation = retainSaturation,
            SaturationStrength = saturationStrength,
            RetainBrightness = retainBrightness,
            BrightnessStrength = brightnessStrength,
            Filter = filter,
            Jobs = jobs,
            Verbose = verbose,
        };
    }

    /// <summary>Consume the value that follows an option.</summary>
    private static string Next(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new CliUsageException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    /// <summary>
    /// Rotate a colour's hue by <paramref name="degrees"/>, keeping its
    /// saturation and value.
    /// </summary>
    /// <remarks>
    /// Used to turn <c>--target-hue-offset</c> into a concrete target colour, so
    /// the rest of the pipeline only ever deals with a source/target pair.
    /// </remarks>
    private static Rgb RotateHue(Rgb colour, double degrees)
    {
        var hsv = Hsv.FromRgb(colour);
        return hsv.RotateHue(degrees).ToRgb();
    }

    private static double ParseDouble(string text, string option)
    {
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            throw new CliUsageException($"{option} expects a number, got '{text}'.");
        }
        return value;
    }

    private static int ParseInt(string text, string option)
    {
        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer,
                          System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            throw new CliUsageException($"{option} expects a whole number, got '{text}'.");
        }
        return value;
    }
}
