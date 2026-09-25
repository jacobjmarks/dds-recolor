using DdsRecolor.Cli;
using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Recolor;

try
{
    CliOptions options = ArgumentParser.Parse(args);

    if (options.ShowHelp)
    {
        Console.WriteLine(ArgumentParser.Usage);
        return 0;
    }

    return Run(options);
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Run with --help for usage.");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Run(CliOptions options)
{
    var shift = new HueShift(options.ToRecolorOptions());

    Console.WriteLine("dds-recolor");
    Console.WriteLine($"  folder      : {options.Folder}");
    Console.WriteLine($"  source      : {options.Source}");
    Console.WriteLine($"  target      : {options.Target}");
    Console.WriteLine($"  hue delta   : {shift.HueDelta:+0.00;-0.00;0.00} degrees");
    Console.WriteLine($"  intensity   : {options.Intensity}");

    // Report what the transform will actually do. Naming the active mode
    // explicitly matters because the default changes all three channels, which is
    // easy to miss when only the hue was on your mind.
    var sourceHsv = Hsv.FromRgb(options.Source);
    var targetHsv = Hsv.FromRgb(options.Target);

    Console.WriteLine($"  saturation  : {(options.RetainSaturation
        ? "RETAINED from source"
        : $"adjusting to target, strength {options.SaturationStrength}")}");
    Console.WriteLine($"  brightness  : {(options.RetainBrightness
        ? "RETAINED from source"
        : $"adjusting to target, strength {options.BrightnessStrength}")}");

    // Warn when a retained channel is the one carrying most of the difference to
    // the target, since the result will then not reach the colour asked for. This
    // is the same confusion the match-based version guarded against, now pointing
    // the other way: retaining is deliberate, but retaining the *wrong* channel is
    // usually a mistake.
    if (options.RetainSaturation || options.RetainBrightness)
    {
        double saturationGap = Math.Abs(targetHsv.S - sourceHsv.S);
        double brightnessGap = Math.Abs(targetHsv.V - sourceHsv.V);
        double hueGap = Math.Abs(Hsv.HueDelta(sourceHsv.H, targetHsv.H)) / 360.0;

        // 0.15 is roughly a fifth of the range, enough that retaining the channel
        // visibly stops the result matching the requested target.
        bool saturationBlocks = options.RetainSaturation && saturationGap >= 0.15;
        bool brightnessBlocks = options.RetainBrightness && brightnessGap >= 0.15;

        if (saturationBlocks || brightnessBlocks)
        {
            Console.WriteLine();
            Console.WriteLine("  NOTE: a retained channel carries most of the difference to");
            Console.WriteLine("        your target, so the result will NOT match it:");
            Console.WriteLine($"          hue        gap {hueGap,6:P0} of range");
            Console.WriteLine($"          saturation gap {saturationGap,6:P0} of range   " +
                              (options.RetainSaturation ? "<-- RETAINED" : "adjusting"));
            Console.WriteLine($"          brightness gap {brightnessGap,6:P0} of range   " +
                              (options.RetainBrightness ? "<-- RETAINED" : "adjusting"));
            Console.WriteLine();
            Console.WriteLine("        The result will keep the source's character in that channel.");
            Console.WriteLine("        To reach the target instead, drop:");
            if (saturationBlocks) Console.WriteLine("          --retain-saturation");
            if (brightnessBlocks) Console.WriteLine("          --retain-brightness");
        }
    }

    Console.WriteLine($"  mode        : {options.Mode.ToString().ToLowerInvariant()}");
    Console.WriteLine($"  alpha       : {options.AlphaMode.ToString().ToLowerInvariant()}");

    if (options.InPlace && !options.AssumeYes)
    {
        // Overwriting originals is the one irreversible thing this tool does.
        // State it plainly rather than burying it in an option list, and require
        // an explicit acknowledgement so a mistyped command cannot silently
        // destroy a texture folder.
        Console.WriteLine();
        Console.WriteLine("  ****************************************************************");
        Console.WriteLine("  *  IN PLACE: the original .dds files will be OVERWRITTEN.       *");
        Console.WriteLine("  *  This cannot be undone and there is no backup.                *");
        Console.WriteLine("  *  Re-run with --out (or drop --in-place) to write alongside.   *");
        Console.WriteLine("  ****************************************************************");
        Console.WriteLine();
        Console.WriteLine("  Type 'yes' to continue, or anything else to abort:");
        Console.Write("  > ");

        string? reply = Console.ReadLine();
        if (!string.Equals(reply?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine("Aborted. Nothing was written.");
            return 4;
        }
        Console.WriteLine();
    }

    // A second in-place run re-applies the same rotation to already-rotated
    // colours. Repeated runs therefore drift further from the original and never
    // converge, and in-place writes are irreversible, so check up front.
    if (options.InPlace && options.Mode == RecolorMode.Block)
    {
        var suspects = RepeatRunCheck.FindSuspects(options);
        if (suspects.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  WARNING: {suspects.Count} texture(s) already look recoloured toward "
                              + $"the target:");
            foreach (var (name, analysis) in suspects.Take(5))
            {
                Console.WriteLine($"    {name}");
                Console.WriteLine($"      {analysis.Describe()}");
            }
            if (suspects.Count > 5)
            {
                Console.WriteLine($"    ... and {suspects.Count - 5} more");
            }
            Console.WriteLine();
            Console.WriteLine("  Applying the shift again WILL rotate them further, and there is");
            Console.WriteLine("  no way to undo it. Abort if you only meant to run this once.");
            Console.WriteLine();

            if (options.AssumeYes)
            {
                Console.WriteLine("  Continuing because --yes was passed.");
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("  Type 'yes' to continue anyway, or anything else to abort:");
                Console.Write("  > ");
                string? second = Console.ReadLine();
                if (!string.Equals(second?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine();
                    Console.WriteLine("Aborted. Nothing was written.");
                    return 4;
                }
                Console.WriteLine();
            }
        }
    }

    Console.WriteLine($"  output      : {(options.InPlace ? "IN PLACE (originals overwritten)" : options.ResolveOutputRoot())}");

    if (shift.IsNoOp)
    {
        Console.WriteLine();
        Console.WriteLine("NOTE: the hue delta is zero, so no texture will change.");
    }

    Console.WriteLine();

    if (options.Mode != RecolorMode.Block)
    {
        Console.Error.WriteLine(
            "error: --mode reencode is not implemented yet; only the block engine is available.");
        Console.Error.WriteLine(
            "       Use --mode block (the default), which preserves file size exactly.");
        return 3;
    }

    var processor = new FolderProcessor(options);
    var progress = new ProgressThrottle(options.Preview);
    RunSummary summary = processor.Run(progress);

    Report(summary, options);

    return summary.Count(FileOutcome.Failed) > 0 ? 1 : 0;
}

static void Report(RunSummary summary, CliOptions options)
{
    Console.WriteLine();
    Console.WriteLine("=== SUMMARY ===");

    if (summary.Total == 0)
    {
        Console.WriteLine("  No .dds files matched.");
        return;
    }

    Console.WriteLine($"  files processed   : {summary.Total}");
    Console.WriteLine($"  recoloured        : {summary.Count(FileOutcome.Written)}");
    Console.WriteLine($"  unchanged         : {summary.Count(FileOutcome.Unchanged)}");
    Console.WriteLine($"  copied through    : {summary.Count(FileOutcome.CopiedThrough)}");
    Console.WriteLine($"  failed            : {summary.Count(FileOutcome.Failed)}");
    Console.WriteLine($"  bytes written     : {summary.BytesWritten:N0}");
    Console.WriteLine($"  elapsed           : {summary.Elapsed.TotalSeconds:F1}s");

    var copied = summary.Results.Where(r => r.Outcome == FileOutcome.CopiedThrough).ToList();
    if (copied.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  formats copied through unchanged (not recolourable by this engine):");
        foreach (var group in copied.GroupBy(r => r.Message ?? "unknown"))
        {
            Console.WriteLine($"    {group.Count(),6}  {group.Key}");
        }
    }

    var failed = summary.Results.Where(r => r.Outcome == FileOutcome.Failed).ToList();
    if (failed.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  failures:");
        foreach (var failure in failed.Take(20))
        {
            Console.WriteLine($"    {failure.RelativePath}");
            Console.WriteLine($"      {failure.Message}");
        }
        if (failed.Count > 20)
        {
            Console.WriteLine($"    ... and {failed.Count - 20} more");
        }
    }

    if (options.Verbose)
    {
        Console.WriteLine();
        Console.WriteLine("  per-file detail:");
        foreach (var result in summary.Results)
        {
            string mark = result.Outcome switch
            {
                FileOutcome.Written => "recoloured",
                FileOutcome.CopiedThrough => "copied    ",
                FileOutcome.Unchanged => "unchanged ",
                _ => "FAILED    ",
            };
            Console.WriteLine($"    {mark}  {result.RelativePath}");
        }
    }
}

/// <summary>
/// Prints periodic progress rather than a line per file, so a 1500-file run
/// does not flood the terminal.
/// </summary>
internal sealed class ProgressThrottle(bool quiet) : IProgress<string>
{
    private int _count;

    public void Report(string value)
    {
        if (quiet) return;

        int count = Interlocked.Increment(ref _count);
        if (count % 250 == 0)
        {
            Console.WriteLine($"  ... {count} files");
        }
    }
}

