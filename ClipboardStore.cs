using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace ClipboardHistory;

public sealed class ClipboardStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipboardHistory");
    private string EntriesFile => Path.Combine(_root, "history.json");
    private string SettingsFile => Path.Combine(_root, "settings.json");
    private string ImagesFolder => Path.Combine(_root, "images");

    public ClipboardStore()
    {
        Directory.CreateDirectory(ImagesFolder);
    }

    public List<ClipEntry> LoadEntries()
    {
        if (!File.Exists(EntriesFile)) return [];

        var json = File.ReadAllText(EntriesFile);
        var entries = JsonSerializer.Deserialize<List<ClipEntry>>(json, JsonOptions)
            ?? throw new InvalidDataException("历史记录文件为空或格式不正确。");

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ImagePath) || !File.Exists(entry.ImagePath)) continue;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(entry.ImagePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            entry.Preview = image;
        }

        return entries;
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFile)) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions)
            ?? throw new InvalidDataException("设置文件为空或格式不正确。");
        settings.RetentionDays = Math.Clamp(settings.RetentionDays, 1, 30);
        return settings;
    }

    public void SaveEntries(IEnumerable<ClipEntry> entries) =>
        SaveJson(EntriesFile, entries);

    public void SaveSettings(AppSettings settings) =>
        SaveJson(SettingsFile, settings);

    public string SaveImage(BitmapSource bitmap)
    {
        var path = Path.Combine(ImagesFolder, $"{Guid.NewGuid():N}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    public void DeleteImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);
        var imageRoot = Path.GetFullPath(ImagesFolder) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(imageRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(tempPath, path, true);
    }
}
