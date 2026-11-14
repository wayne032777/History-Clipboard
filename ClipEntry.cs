using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace ClipboardHistory;

public sealed class ClipEntry : INotifyPropertyChanged
{
    private bool _isPinned;
    private string _text = "";
    private string _ocrText = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string ImagePath { get; set; } = "";

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string OcrText
    {
        get => _ocrText;
        set
        {
            if (_ocrText == value) return;
            _ocrText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value) return;
            _isPinned = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public BitmapSource? Preview { get; set; }

    [JsonIgnore]
    public string Summary => !string.IsNullOrEmpty(Text)
        ? Text
        : !string.IsNullOrWhiteSpace(OcrText) ? OcrText : "图片（未识别到文字）";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class AppSettings
{
    public int RetentionDays { get; set; } = 3;
    public bool StartWithWindows { get; set; } = true;
}
