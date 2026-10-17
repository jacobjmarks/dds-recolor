using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using DdsRecolor.Core.Formats;
using DdsRecolor.Core.Io;
using DdsRecolor.Core.Recolor;

namespace DdsRecolor.Gui;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TextureEntry> _entries = [];
    private readonly GuiSessionSettings _sessionSettings = GuiSessionSettings.Load();
    private DdsFile? _activeFile;
    private DdsPreviewImage? _originalPreview;
    private WriteableBitmap? _originalBitmap;
    private WriteableBitmap? _recoloredBitmap;
    private CancellationTokenSource? _previewCancellation;
    private string? _rootFolder;
    private int _loadGeneration;
    private long _previewGeneration;
    private bool _showOriginal;
    private bool _isInitialized;
    private bool _settingParameterText;
    private bool _applyingParameterText;
    private bool _sessionReady;
    private string? _lastOpenedPath;
    private bool _openedAsSingleFile;
    private string _lastValidArguments = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        FileListBox.ItemsSource = _entries;
        _lastOpenedPath = _sessionSettings.OpenPath;
        _openedAsSingleFile = _sessionSettings.OpenedAsSingleFile;
        _lastValidArguments = _sessionSettings.LastValidArguments;
        if (_lastOpenedPath is not null)
        {
            _rootFolder = _openedAsSingleFile
                ? Path.GetDirectoryName(_lastOpenedPath)
                : _lastOpenedPath;
        }

        SourcePicker.PropertyChanged += ColorPickerPropertyChanged;
        TargetPicker.PropertyChanged += ColorPickerPropertyChanged;
        _isInitialized = true;
        UpdateSliderLabels();
        if (string.IsNullOrWhiteSpace(_lastValidArguments))
        {
            UpdateParameterFragment();
        }
        else if (CliPreviewArguments.TryParse(
            _lastValidArguments, _rootFolder, out RecolorOptions savedOptions,
            out string message, out bool isWarning))
        {
            ApplyParsedOptions(savedOptions, message, isWarning);
        }
        else
        {
            SetParameterIssue($"Saved settings could not be loaded: {message}", isWarning: false);
            UpdateParameterFragment();
        }

        Opened += RestoreSession;
        _sessionReady = true;
    }

    private void MainWindowClosing(object? sender, WindowClosingEventArgs e) => SaveSession();

    private void RestoreSession(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastOpenedPath)) return;

        try
        {
            if (_openedAsSingleFile)
            {
                if (!File.Exists(_lastOpenedPath))
                {
                    StatusText.Text = $"Last opened file no longer exists: {_lastOpenedPath}";
                    return;
                }

                string folder = Path.GetDirectoryName(_lastOpenedPath) ?? Environment.CurrentDirectory;
                SetEntries(folder, [_lastOpenedPath], _lastOpenedPath, openedAsSingleFile: true);
                return;
            }

            if (!Directory.Exists(_lastOpenedPath))
            {
                StatusText.Text = $"Last opened folder no longer exists: {_lastOpenedPath}";
                return;
            }

            string[] paths = EnumerateDdsFiles(_lastOpenedPath);
            SetEntries(_lastOpenedPath, paths, _lastOpenedPath, openedAsSingleFile: false,
                activeFilePath: _sessionSettings.ActiveFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not restore last location: {ex.Message}";
        }
    }

    private async void OpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Open texture folder", AllowMultiple = false });
        if (folders.Count == 0) return;

        string folder = folders[0].Path.LocalPath;
        try
        {
            SetEntries(folder, EnumerateDdsFiles(folder), folder, openedAsSingleFile: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not read folder: {ex.Message}";
        }
    }

    private async void OpenFileClicked(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Open DDS texture",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("DDS textures") { Patterns = ["*.dds"] }],
            });
        if (files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        string folder = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        SetEntries(folder, [path], path, openedAsSingleFile: true);
    }

    private static string[] EnumerateDdsFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".dds", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void SetEntries(string folder, IEnumerable<string> paths, string openPath,
                            bool openedAsSingleFile, string? activeFilePath = null)
    {
        _rootFolder = Path.GetFullPath(folder);
        _lastOpenedPath = Path.GetFullPath(openPath);
        _openedAsSingleFile = openedAsSingleFile;
        FolderPathText.Text = _rootFolder;
        _entries.Clear();
        foreach (string path in paths)
        {
            _entries.Add(new TextureEntry(path, Path.GetRelativePath(_rootFolder, path)));
        }

        FileCountText.Text = _entries.Count.ToString(CultureInfo.InvariantCulture);
        FileListBox.SelectedItem = activeFilePath is null
            ? _entries.FirstOrDefault()
            : _entries.FirstOrDefault(entry => string.Equals(
                Path.GetFullPath(entry.Path), Path.GetFullPath(activeFilePath), StringComparison.OrdinalIgnoreCase))
                ?? _entries.FirstOrDefault();
        if (_entries.Count == 0) ClearPreview("No DDS files found in this folder");
        SaveSession();
    }

    private async void FileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FileListBox.SelectedItem is not TextureEntry entry) return;
        SaveSession();

        int generation = ++_loadGeneration;
        _previewCancellation?.Cancel();
        _showOriginal = false;
        _activeFile = null;
        _originalPreview = null;
        _originalBitmap = null;
        _recoloredBitmap = null;
        PreviewImage.Source = null;
        PreviewModeBadge.IsVisible = false;
        PreviewTitle.Text = entry.Name;
        PreviewDetails.Text = entry.RelativePath;
        PreviewMessage.Text = "Loading DDS…";

        try
        {
            DdsFile file = await Task.Run(() => DdsFile.Load(entry.Path));
            if (generation != _loadGeneration) return;

            _activeFile = file;
            PreviewTitle.Text = entry.Name;
            PreviewDetails.Text = $"{file.Header.Width} × {file.Header.Height}  |  {file.Header.Format.DisplayName()}";

            if (!file.CanRecolorInPlace)
            {
                PreviewMessage.Text = $"{file.Header.Format.DisplayName()} is recognized, but recoloring is not supported.";
                StatusText.Text = "This file can’t be previewed by the current recolor engine.";
                return;
            }

            DdsPreviewImage preview = await Task.Run(() => DdsPreviewDecoder.Decode(file));
            if (generation != _loadGeneration) return;

            _originalPreview = preview;
            _originalBitmap = CreateBitmap(preview);
            _recoloredBitmap = _originalBitmap;
            PreviewImage.Source = _recoloredBitmap;
            PreviewMessage.Text = string.Empty;
            StatusText.Text = $"Loaded {entry.RelativePath}";
            SchedulePreview();
        }
        catch (Exception ex) when (ex is DdsFormatException or IOException or UnauthorizedAccessException)
        {
            if (generation != _loadGeneration) return;
            PreviewMessage.Text = ex.Message;
            PreviewDetails.Text = "Could not read this DDS file";
            StatusText.Text = "The file could not be loaded.";
        }
    }

    private void ClearPreview(string message)
    {
        ++_loadGeneration;
        _previewCancellation?.Cancel();
        _showOriginal = false;
        _activeFile = null;
        _originalPreview = null;
        _originalBitmap = null;
        _recoloredBitmap = null;
        PreviewImage.Source = null;
        PreviewTitle.Text = "No texture selected";
        PreviewDetails.Text = string.Empty;
        PreviewMessage.Text = message;
        PreviewModeBadge.IsVisible = false;
        StatusText.Text = "Ready";
    }

    private void ColorPickerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Color") OptionsChanged();
    }

    private void OptionsTextChanged(object? sender, TextChangedEventArgs e) => OptionsChanged();

    private void SliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isInitialized) return;
        UpdateSliderLabels();
        OptionsChanged();
    }

    private void RetainSettingChanged(object? sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        SaturationSlider.IsEnabled = RetainSaturationCheckBox.IsChecked != true;
        BrightnessSlider.IsEnabled = RetainBrightnessCheckBox.IsChecked != true;
        OptionsChanged();
    }

    private void AlphaModeChanged(object? sender, SelectionChangedEventArgs e) => OptionsChanged();

    private void OptionsChanged()
    {
        if (!_isInitialized || _applyingParameterText) return;
        UpdateParameterFragment();
        SchedulePreview();
    }

    private void UpdateSliderLabels()
    {
        ToleranceValue.Text = $"{ToleranceSlider.Value:0}°";
        SaturationValue.Text = SaturationSlider.Value.ToString("0.00", CultureInfo.InvariantCulture);
        BrightnessValue.Text = BrightnessSlider.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void UpdateParameterFragment()
    {
        if (!TryReadOptions(out RecolorOptions options, out string error))
        {
            SetParameterIssue(error, isWarning: false);
            return;
        }

        UpdateAppliedChannelsText(options);
        _lastValidArguments = CliPreviewArguments.Format(options);
        SetParameterText(_lastValidArguments);
        SetParameterIssue(string.Empty, isWarning: false);
        if (_sessionReady) SaveSession();
    }

    private void UpdateAppliedChannelsText(RecolorOptions options)
    {
        string saturation = options.RetainSaturation
            ? "retained"
            : options.SaturationStrength.ToString("P0", CultureInfo.InvariantCulture);
        string brightness = options.RetainBrightness
            ? "retained"
            : options.BrightnessStrength.ToString("P0", CultureInfo.InvariantCulture);
        AppliedChannelsText.Text = $"Saturation {saturation}  ·  Brightness {brightness}";
    }

    private void ParameterTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _settingParameterText || _applyingParameterText) return;
        if (!CliPreviewArguments.TryParse(ParameterTextBox.Text ?? string.Empty, _rootFolder,
            out RecolorOptions options, out string message, out bool isWarning))
        {
            SetParameterIssue(message, isWarning: false);
            return;
        }

        ApplyParsedOptions(options, message, isWarning);
    }

    private void ApplyParsedOptions(RecolorOptions options, string message, bool isWarning)
    {
        _applyingParameterText = true;
        SourcePicker.Color = Color.FromRgb(options.Source.R, options.Source.G, options.Source.B);
        TargetPicker.Color = Color.FromRgb(options.Target.R, options.Target.G, options.Target.B);
        IntensityBox.Text = options.Intensity.ToString("G", CultureInfo.InvariantCulture);
        AlphaModeComboBox.SelectedIndex = options.AlphaMode == AlphaMode.Premultiplied ? 1 : 0;
        ToleranceSlider.Value = options.HueToleranceDegrees;
        RetainSaturationCheckBox.IsChecked = options.RetainSaturation;
        SaturationSlider.Value = options.SaturationStrength;
        RetainBrightnessCheckBox.IsChecked = options.RetainBrightness;
        BrightnessSlider.Value = options.BrightnessStrength;
        SaturationSlider.IsEnabled = !options.RetainSaturation;
        BrightnessSlider.IsEnabled = !options.RetainBrightness;
        _applyingParameterText = false;

        UpdateSliderLabels();
        _lastValidArguments = CliPreviewArguments.Format(options);
        SetParameterText(_lastValidArguments);
        SetParameterIssue(message, isWarning);
        CopyParametersButton.IsEnabled = true;
        SchedulePreview();
        if (_sessionReady) SaveSession();
    }

    private void SetParameterText(string text)
    {
        _settingParameterText = true;
        ParameterTextBox.Text = text;
        _settingParameterText = false;
        CopyParametersButton.IsEnabled = !string.IsNullOrWhiteSpace(text);
    }

    private void SetParameterIssue(string message, bool isWarning)
    {
        ParameterIssueText.Text = message;
        ParameterIssueText.IsVisible = !string.IsNullOrWhiteSpace(message);
        Color color = isWarning ? Color.Parse("#E2A84B") : Color.Parse("#F06B68");
        ParameterIssueText.Foreground = new SolidColorBrush(color);
        ParameterTextBox.BorderBrush = new SolidColorBrush(color);
        if (string.IsNullOrWhiteSpace(message))
        {
            ParameterTextBox.ClearValue(Border.BorderBrushProperty);
        }
        CopyParametersButton.IsEnabled =
            !string.IsNullOrWhiteSpace(ParameterTextBox.Text) && (string.IsNullOrWhiteSpace(message) || isWarning);
    }

    private void SaveSession()
    {
        if (!_sessionReady) return;
        _sessionSettings.OpenPath = _lastOpenedPath;
        _sessionSettings.OpenedAsSingleFile = _openedAsSingleFile;
        _sessionSettings.ActiveFilePath = (FileListBox.SelectedItem as TextureEntry)?.Path;
        _sessionSettings.LastValidArguments = _lastValidArguments;
        try
        {
            _sessionSettings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not save GUI settings: {ex.Message}";
        }
    }

    private bool TryReadOptions(out RecolorOptions options, out string error)
    {
        if (!double.TryParse(IntensityBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double intensity)
            || !double.IsFinite(intensity) || intensity < 0)
        {
            options = null!;
            error = "Intensity must be a nonnegative number.";
            return false;
        }

        options = new RecolorOptions
        {
            Source = ToRgb(SourcePicker.Color),
            Target = ToRgb(TargetPicker.Color),
            Intensity = intensity,
            AlphaMode = AlphaModeComboBox.SelectedIndex == 1 ? AlphaMode.Premultiplied : AlphaMode.Straight,
            HueToleranceDegrees = ToleranceSlider.Value,
            RetainSaturation = RetainSaturationCheckBox.IsChecked == true,
            SaturationStrength = SaturationSlider.Value,
            RetainBrightness = RetainBrightnessCheckBox.IsChecked == true,
            BrightnessStrength = BrightnessSlider.Value,
        };
        error = string.Empty;
        return true;
    }

    private static Rgb ToRgb(Color color) => new(color.R, color.G, color.B);

    private void SchedulePreview()
    {
        if (_activeFile is null || !_activeFile.CanRecolorInPlace || _originalPreview is null) return;
        if (!TryReadOptions(out RecolorOptions options, out string error))
        {
            StatusText.Text = error;
            return;
        }

        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        long generation = Interlocked.Increment(ref _previewGeneration);
        DdsFile file = _activeFile;
        StatusText.Text = "Rendering preview…";
        _ = RenderPreviewAsync(file, options, generation, cancellation.Token);
    }

    private async Task RenderPreviewAsync(DdsFile file, RecolorOptions options, long generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(90, token);
            (DdsPreviewImage image, int modified, int collisions) = await Task.Run(() =>
            {
                var result = file.Recolor(new HueShift(options));
                return (DdsPreviewDecoder.Decode(DdsFile.Load(result.Data)),
                    result.BlocksModified, result.BlocksWithEndpointCollision);
            }, token);

            if (token.IsCancellationRequested || generation != Volatile.Read(ref _previewGeneration)) return;
            _recoloredBitmap = CreateBitmap(image);
            if (!_showOriginal) PreviewImage.Source = _recoloredBitmap;
            StatusText.Text = $"Preview updated · {modified:N0} blocks changed · {collisions:N0} endpoint collisions repaired";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is DdsFormatException or IOException)
        {
            if (generation == Volatile.Read(ref _previewGeneration)) StatusText.Text = ex.Message;
        }
    }

    private static WriteableBitmap CreateBitmap(DdsPreviewImage image)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer framebuffer = bitmap.Lock();
        for (int y = 0; y < image.Height; y++)
        {
            Marshal.Copy(image.RgbaPixels, y * image.Width * 4,
                IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes), image.Width * 4);
        }
        return bitmap;
    }

    private void PreviewPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_originalBitmap is null || !IsPointerOverImage(e.GetPosition(PreviewImage))) return;
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(PreviewImage).Properties.IsLeftButtonPressed) return;

        _showOriginal = true;
        PreviewImage.Source = _originalBitmap;
        PreviewModeBadge.IsVisible = true;
        e.Pointer.Capture(PreviewImage);
        e.Handled = true;
    }

    private void PreviewReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_showOriginal) return;
        RestoreRecoloredPreview();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void PreviewCaptureLost(object? sender, PointerCaptureLostEventArgs e) => RestoreRecoloredPreview();

    private bool IsPointerOverImage(Point point)
    {
        if (_originalPreview is null || PreviewImage.Bounds.Width <= 0 || PreviewImage.Bounds.Height <= 0) return false;

        double scale = Math.Min(
            PreviewImage.Bounds.Width / _originalPreview.Width,
            PreviewImage.Bounds.Height / _originalPreview.Height);
        double width = _originalPreview.Width * scale;
        double height = _originalPreview.Height * scale;
        double left = (PreviewImage.Bounds.Width - width) / 2;
        double top = (PreviewImage.Bounds.Height - height) / 2;
        return point.X >= left && point.X <= left + width && point.Y >= top && point.Y <= top + height;
    }

    private void RestoreRecoloredPreview()
    {
        _showOriginal = false;
        PreviewModeBadge.IsVisible = false;
        if (_recoloredBitmap is not null) PreviewImage.Source = _recoloredBitmap;
    }

    private async void CopyParametersClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ParameterTextBox.Text)) return;
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetValueAsync(DataFormat.Text, ParameterTextBox.Text);
        StatusText.Text = "CLI parameters copied.";
    }
}

public sealed class TextureEntry(string path, string relativePath)
{
    public string Path { get; } = path;
    public string Name { get; } = System.IO.Path.GetFileName(path);
    public string RelativePath { get; } = relativePath;
}