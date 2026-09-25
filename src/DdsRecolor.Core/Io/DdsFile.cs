namespace DdsRecolor.Core.Io;

using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Recolor;

/// <summary>A DDS file loaded into memory, with its parsed header.</summary>
public sealed class DdsFile
{
    private DdsFile(byte[] data, DdsHeader header)
    {
        Data = data;
        Header = header;
    }

    /// <summary>Raw file bytes, including the header.</summary>
    public byte[] Data { get; }

    /// <summary>Parsed header and mip offset table.</summary>
    public DdsHeader Header { get; }

    /// <summary>Load and validate a DDS file from disk.</summary>
    public static DdsFile Load(string path) => Load(File.ReadAllBytes(path));

    /// <summary>Parse and validate DDS bytes already in memory.</summary>
    /// <exception cref="DdsFormatException">The bytes are not a supported DDS file.</exception>
    public static DdsFile Load(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var header = DdsHeader.Parse(bytes);

        // Validate the length before anything else uses the offset table. A
        // mismatch means our understanding of the mip layout is wrong for this
        // file, and rewriting it would corrupt unrelated bytes.
        header.ValidateFileSize(bytes.Length);

        return new DdsFile(bytes, header);
    }

    /// <summary>True when the default block-level engine can recolor this file.</summary>
    public bool CanRecolorInPlace => BlockEndpointRecolorer.CanHandle(Header.Format);

    /// <summary>Recolor using the block-level endpoint engine.</summary>
    public RecolorResult Recolor(HueShift shift) =>
        new BlockEndpointRecolorer(shift).Recolor(Data, Header);

    /// <summary>Write bytes to disk, creating the directory if needed.</summary>
    public static void Write(string path, byte[] data)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(path, data);
    }

    /// <summary>
    /// Replace an existing file with new bytes, via a temporary file and an
    /// atomic move.
    /// </summary>
    /// <remarks>
    /// Used by <c>--in-place</c>. A partial write would destroy the user's
    /// original texture, so the new content is fully flushed to a sibling
    /// temporary file first and only then swapped into place.
    /// </remarks>
    public static void WriteAtomic(string path, byte[] data)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = fullPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
