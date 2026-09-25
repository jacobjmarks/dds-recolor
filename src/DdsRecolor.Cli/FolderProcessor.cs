namespace DdsRecolor.Cli;

using System.Collections.Concurrent;
using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Io;
using DdsRecolor.Core.Recolor;

/// <summary>Outcome of processing one file.</summary>
public enum FileOutcome
{
    /// <summary>Recoloured and written.</summary>
    Written,

    /// <summary>Copied through unchanged because the format cannot be recoloured.</summary>
    CopiedThrough,

    /// <summary>The texture was already the right colour, so nothing needed writing.</summary>
    Unchanged,

    /// <summary>Failed; the reason is in <see cref="FileResult.Message"/>.</summary>
    Failed,
}

/// <summary>Per-file result.</summary>
public sealed record FileResult(
    string RelativePath,
    FileOutcome Outcome,
    string? Message = null,
    long BytesWritten = 0,
    int BlocksModified = 0);

/// <summary>Aggregate result of a whole run.</summary>
public sealed class RunSummary
{
    public List<FileResult> Results { get; } = [];
    public TimeSpan Elapsed { get; set; }
    public string? OutputRoot { get; set; }

    public int Count(FileOutcome outcome) => Results.Count(r => r.Outcome == outcome);
    public int Total => Results.Count;
    public long BytesWritten => Results.Sum(r => r.BytesWritten);
}

/// <summary>
/// Walks a folder tree, recolours every .dds file, and writes results while
/// mirroring the directory structure.
/// </summary>
public sealed class FolderProcessor
{
    private readonly CliOptions _options;
    private readonly HueShift _shift;

    public FolderProcessor(CliOptions options)
    {
        _options = options;
        _shift = new HueShift(options.ToRecolorOptions());
    }

    /// <summary>Enumerate the .dds files that the options select.</summary>
    public List<string> EnumerateFiles()
    {
        var files = new List<string>();

        foreach (string path in Directory.EnumerateFiles(
                     _options.Folder, "*.dds", SearchOption.AllDirectories))
        {
            if (_options.Filter is not null &&
                !MatchGlob(Path.GetFileName(path), _options.Filter))
            {
                continue;
            }
            files.Add(path);
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>Process every selected file.</summary>
    public RunSummary Run(IProgress<string>? progress = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var summary = new RunSummary();

        List<string> files = EnumerateFiles();
        if (files.Count == 0)
        {
            summary.Elapsed = stopwatch.Elapsed;
            return summary;
        }

        // Each file is independent, and the transform is deterministic per file,
        // so parallelism cannot change the output bytes. Results are collected
        // and then sorted by path so the reported order is stable regardless of
        // scheduling.
        var results = new ConcurrentBag<FileResult>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.Jobs,
        };

        Parallel.ForEach(files, parallelOptions, path =>
        {
            FileResult result = ProcessFile(path);
            results.Add(result);
            progress?.Report(result.RelativePath);
        });

        summary.Results.AddRange(results.OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase));
        summary.Elapsed = stopwatch.Elapsed;
        return summary;
    }

    private FileResult ProcessFile(string path)
    {
        string relative = Path.GetRelativePath(_options.Folder, path);

        try
        {
            var file = DdsFile.Load(path);

            if (!file.CanRecolorInPlace)
            {
                // Never guess at a format we cannot rewrite. Copy it through
                // byte-for-byte and report, so the run still mirrors the input
                // tree and nothing is silently lost.
                return CopyThrough(file, path, relative);
            }

            var result = file.Recolor(_shift);

            if (_options.InPlace)
            {
                if (result.BlocksModified == 0)
                {
                    return new FileResult(relative, FileOutcome.Unchanged);
                }

                DdsFile.WriteAtomic(path, result.Data);
                return new FileResult(relative, FileOutcome.Written,
                    BytesWritten: result.Data.Length,
                    BlocksModified: result.BlocksModified);
            }

            string destination = BuildOutputPath(relative, path);
            DdsFile.Write(destination, result.Data);

            return new FileResult(relative, FileOutcome.Written,
                BytesWritten: result.Data.Length,
                BlocksModified: result.BlocksModified);
        }
        catch (Exception ex) when (ex is DdsFormatException or IOException or UnauthorizedAccessException)
        {
            return new FileResult(relative, FileOutcome.Failed, ex.Message);
        }
    }

    private FileResult CopyThrough(DdsFile file, string path, string relative)
    {
        string message =
            $"{file.Header.Format.DisplayName()} cannot be recoloured by the block engine; " +
            "copied through unchanged.";

        if (_options.InPlace)
        {
            // Nothing to write: the file is already what it should be.
            return new FileResult(relative, FileOutcome.CopiedThrough, message);
        }

        DdsFile.Write(BuildOutputPath(relative, path), file.Data);
        return new FileResult(relative, FileOutcome.CopiedThrough, message,
            BytesWritten: file.Data.Length);
    }

    private string BuildOutputPath(string relative, string sourcePath)
    {
        string root = _options.ResolveOutputRoot();
        return Path.Combine(root, relative);
    }

    /// <summary>
    /// Case-insensitive glob supporting <c>*</c> and <c>?</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately hand-rolled rather than pulling in a dependency: only two
    /// wildcards are needed, and <c>FileSystemName.MatchesSimpleExpression</c>
    /// is available in the BCL if this ever needs expanding.
    /// </remarks>
    internal static bool MatchGlob(string name, string pattern)
    {
        return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
            pattern, name, ignoreCase: true);
    }
}
