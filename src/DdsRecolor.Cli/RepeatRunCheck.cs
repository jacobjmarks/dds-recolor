namespace DdsRecolor.Cli;

using DdsRecolor.Core.Io;
using DdsRecolor.Core.Recolor;

/// <summary>
/// Pre-flight check for an <c>--in-place</c> run, looking for textures that
/// already appear to have been recoloured toward the target colour.
/// </summary>
/// <remarks>
/// The block engine applies a fixed hue rotation, so applying it twice rotates
/// twice. In-place writes are irreversible, so a user who re-runs the tool by
/// mistake would progressively distort their textures with no way to recover.
///
/// This is a heuristic and never blocks on its own; see
/// <see cref="RecolorDetector"/> for why it only warns.
/// </remarks>
internal static class RepeatRunCheck
{
    /// <summary>
    /// How many files to inspect before drawing a conclusion.
    /// </summary>
    /// <remarks>
    /// The check exists to catch an obvious mistake, so sampling the first few
    /// files is enough and keeps a run over thousands of textures fast. If a
    /// set is mixed, some files being already-recoloured is still worth
    /// surfacing.
    /// </remarks>
    private const int MaxFilesToInspect = 40;

    /// <summary>
    /// Files that look like they have already been recoloured, with the analysis
    /// that led to that conclusion.
    /// </summary>
    /// <returns>An empty list when nothing suspicious was found.</returns>
    public static List<(string Name, RecolorDetector.Analysis Analysis)> FindSuspects(
        CliOptions options)
    {
        var suspects = new List<(string, RecolorDetector.Analysis)>();

        if (options.Mode != RecolorMode.Block)
        {
            return suspects;
        }

        var processor = new FolderProcessor(options);
        List<string> files;

        try
        {
            files = processor.EnumerateFiles();
        }
        catch (Exception)
        {
            // A failure here must not block the run; the actual processing pass
            // will report per-file errors properly.
            return suspects;
        }

        foreach (string path in files.Take(MaxFilesToInspect))
        {
            try
            {
                var file = DdsFile.Load(path);
                if (!file.CanRecolorInPlace) continue;

                var analysis = RecolorDetector.Analyse(
                    file, options.Source, options.Target);

                if (analysis.LooksAlreadyRecolored)
                {
                    suspects.Add((Path.GetFileName(path), analysis));
                }
            }
            catch (Exception)
            {
                // Unreadable or unsupported files are the processing pass's
                // problem to report. Skip them here.
            }
        }

        return suspects;
    }
}
