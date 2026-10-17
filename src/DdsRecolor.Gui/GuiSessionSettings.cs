using System.Text.Json;

namespace DdsRecolor.Gui;

public sealed class GuiSessionSettings
{
    public string? OpenPath { get; set; }
    public bool OpenedAsSingleFile { get; set; }
    public string? ActiveFilePath { get; set; }
    public string LastValidArguments { get; set; } = string.Empty;

    public static GuiSessionSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return JsonSerializer.Deserialize<GuiSessionSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new GuiSessionSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this));
    }

    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DdsRecolor", "gui-settings.json");
}