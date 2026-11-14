using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ClipboardHistory;

public partial class MainWindow : Window
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly ClipboardStore _store = new();
    private readonly ClipboardOcr _ocr = new();
    private readonly ObservableCollection<ClipEntry> _entries = [];
    private readonly ICollectionView _historyView;
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Forms.NotifyIcon _trayIcon = new();
    private AppSettings _settings = new();
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private IntPtr _lastExternalWindow;
    private uint _lastOwnClipboardSequence;
    private DateTime _lastCleanupUtc;
    private bool _initializing = true;
    private bool _allowExit;

    public MainWindow()
    {
        InitializeComponent();
        _historyView = System.Windows.Data.CollectionViewSource.GetDefaultView(_entries);
        _historyView.SortDescriptions.Add(new SortDescription(nameof(ClipEntry.CreatedAt), ListSortDirection.Descending));
        _historyView.Filter = MatchesSearch;
        HistoryList.ItemsSource = _historyView;

        try
        {
            _settings = _store.LoadSettings();
            foreach (var entry in _store.LoadEntries())
            {
                _entries.Add(entry);
            }
        }
        catch (IOException ex)
        {
            MessageBox.Show($"读取本地历史记录失败：{ex.Message}", "历史粘贴板", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (System.Text.Json.JsonException ex)
        {
            MessageBox.Show($"历史记录文件格式有误：{ex.Message}", "历史粘贴板", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(ex.Message, "历史粘贴板", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        ApplySettingsToControls();
        ConfigureTrayIcon();
        _foregroundTimer.Tick += ForegroundTimer_Tick;
        _foregroundTimer.Start();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        UpdateEmptyMessage();
        TrySetStartup(_settings.StartWithWindows);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source.AddHook(WindowMessageHook);
        if (!AddClipboardFormatListener(_windowHandle))
        {
            SetStatus("无法启动剪贴板监听，请重新启动软件。");
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CleanupExpiredEntries();
        foreach (var entry in _entries.Where(item => !string.IsNullOrWhiteSpace(item.ImagePath)
                     && string.IsNullOrWhiteSpace(item.OcrText)).ToArray())
        {
            _ = RecognizeEntryAsync(entry);
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate)
        {
            _ = CaptureClipboardAsync();
        }
        return IntPtr.Zero;
    }

    private async Task CaptureClipboardAsync()
    {
        var sequence = GetClipboardSequenceNumber();
        if (_lastOwnClipboardSequence != 0 && sequence == _lastOwnClipboardSequence) return;

        try
        {
            var text = Clipboard.ContainsText(TextDataFormat.UnicodeText)
                ? Clipboard.GetText(TextDataFormat.UnicodeText)
                : "";
            var bitmap = Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
            if (string.IsNullOrEmpty(text) && bitmap is null) return;

            var entry = new ClipEntry
            {
                CreatedAt = DateTime.Now,
                Text = text,
                ImagePath = bitmap is null ? "" : _store.SaveImage(bitmap),
                Preview = bitmap
            };
            if (entry.Preview is not null && entry.Preview.CanFreeze)
            {
                entry.Preview.Freeze();
            }

            _entries.Insert(0, entry);
            var saved = SaveEntries();
            UpdateEmptyMessage();
            if (!string.IsNullOrWhiteSpace(entry.ImagePath))
            {
                await RecognizeEntryAsync(entry);
            }
            else if (saved)
            {
                SetStatus($"已保存 {entry.CreatedAt:HH:mm:ss} 的剪贴板内容。");
            }
        }
        catch (ExternalException ex)
        {
            SetStatus($"读取剪贴板失败：{ex.Message}");
        }
        catch (IOException ex)
        {
            SetStatus($"保存剪贴板内容失败：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            SetStatus($"无法保存剪贴板内容：{ex.Message}");
        }
    }

    private async Task RecognizeEntryAsync(ClipEntry entry)
    {
        try
        {
            entry.OcrText = await _ocr.RecognizeAsync(entry.ImagePath);
            if (!_entries.Contains(entry)) return;
            var saved = SaveEntries();
            _historyView.Refresh();
            if (saved)
            {
                SetStatus(string.IsNullOrWhiteSpace(entry.OcrText)
                    ? "图片已保存，但没有识别到可搜索的文字。"
                    : "图片文字识别完成，可以搜索图片中的文字。");
            }
            UpdateEmptyMessage();
        }
        catch (COMException ex)
        {
            SetStatus($"图片文字识别失败：{ex.Message}");
        }
        catch (IOException ex)
        {
            SetStatus($"读取图片进行文字识别失败：{ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message);
        }
    }

    private bool SaveEntries()
    {
        try
        {
            _store.SaveEntries(_entries);
            return true;
        }
        catch (IOException ex)
        {
            SetStatus($"写入历史记录失败：{ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            SetStatus($"没有权限保存历史记录：{ex.Message}");
            return false;
        }
    }

    private bool MatchesSearch(object item)
    {
        if (item is not ClipEntry entry) return false;
        var query = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return true;
        return entry.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || entry.OcrText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_historyView is null || EmptyMessage is null) return;
        _historyView?.Refresh();
        UpdateEmptyMessage();
    }

    private void EntryCard_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FindButtonAncestor(e.OriginalSource as DependencyObject) is not null) return;
        if (sender is Border border && border.DataContext is ClipEntry entry)
        {
            _ = PasteEntryAsync(entry);
        }
    }

    private static Button? FindButtonAncestor(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Button button) return button;
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    private async Task PasteEntryAsync(ClipEntry entry)
    {
        try
        {
            var data = new DataObject();
            if (!string.IsNullOrEmpty(entry.Text))
            {
                data.SetText(entry.Text, TextDataFormat.UnicodeText);
            }
            if (!string.IsNullOrWhiteSpace(entry.ImagePath) && File.Exists(entry.ImagePath))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(entry.ImagePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                data.SetImage(image);
            }
            Clipboard.SetDataObject(data, true);
            _lastOwnClipboardSequence = GetClipboardSequenceNumber();

            if (_lastExternalWindow == IntPtr.Zero || !IsWindow(_lastExternalWindow))
            {
                SetStatus("內容已放回剪贴板；请切换到需要输入的窗口后按 Ctrl+V。");
                return;
            }

            ShowWindow(_lastExternalWindow, 9);
            if (!SetForegroundWindow(_lastExternalWindow))
            {
                SetStatus("内容已放回剪贴板，但 Windows 未允许自动切换窗口；请切换后按 Ctrl+V。");
                return;
            }
            await Task.Delay(180);
            Forms.SendKeys.SendWait("^v");
            SetStatus("已粘贴到上一个使用的窗口。");
        }
        catch (ExternalException ex)
        {
            SetStatus($"粘贴失败：{ex.Message}");
        }
        catch (IOException ex)
        {
            SetStatus($"读取图片失败：{ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus($"粘贴失败：{ex.Message}");
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ClipEntry entry })
        {
            entry.IsPinned = !entry.IsPinned;
            if (SaveEntries()) SetStatus(entry.IsPinned ? "已置顶。" : "已取消置顶。");
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ClipEntry entry }) return;
        _entries.Remove(entry);
        try
        {
            _store.DeleteImage(entry.ImagePath);
        }
        catch (IOException ex)
        {
            SetStatus($"记录已删除，但图片文件未能清理：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            SetStatus($"记录已删除，但图片文件没有权限清理：{ex.Message}");
        }
        SaveEntries();
        UpdateEmptyMessage();
    }

    private void RetentionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || RetentionCombo.SelectedItem is not ComboBoxItem item) return;
        var custom = Equals(item.Tag, "custom");
        CustomDaysBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (!custom && int.TryParse(item.Tag?.ToString(), out var days))
        {
            _settings.RetentionDays = days;
            CustomDaysBox.Text = days.ToString();
            PersistRetentionSettings();
        }
        else if (custom)
        {
            if (_settings.RetentionDays is 1 or 3 or 5) _settings.RetentionDays = 7;
            CustomDaysBox.Text = _settings.RetentionDays.ToString();
        }
    }

    private void CustomDaysBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing || RetentionCombo.SelectedItem is not ComboBoxItem { Tag: "custom" }) return;
        if (!int.TryParse(CustomDaysBox.Text, out var days) || days < 1 || days > 30)
        {
            SetStatus("自定义保留时间必须是 1 到 30 之间的整数。");
            return;
        }
        if (_settings.RetentionDays == days) return;
        _settings.RetentionDays = days;
        PersistRetentionSettings();
    }

    private void PersistRetentionSettings()
    {
        try
        {
            _store.SaveSettings(_settings);
            CleanupExpiredEntries();
            SetStatus($"历史记录保留时间已设为 {_settings.RetentionDays} 天。");
        }
        catch (IOException ex)
        {
            SetStatus($"保存设置失败：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            SetStatus($"没有权限保存设置：{ex.Message}");
        }
    }

    private void ApplySettingsToControls()
    {
        _initializing = true;
        var selected = _settings.RetentionDays switch
        {
            1 => 0,
            3 => 1,
            5 => 2,
            _ => 3
        };
        RetentionCombo.SelectedIndex = selected;
        CustomDaysBox.Text = _settings.RetentionDays.ToString();
        CustomDaysBox.Visibility = selected == 3 ? Visibility.Visible : Visibility.Collapsed;
        StartupCheck.IsChecked = _settings.StartWithWindows;
        _initializing = false;
    }

    private void StartupCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing || StartupCheck.IsChecked is not bool enabled) return;
        _settings.StartWithWindows = enabled;
        try
        {
            StartupRegistration.SetEnabled(enabled);
            _store.SaveSettings(_settings);
            SetStatus(enabled ? "已开启开机自动启动。" : "已关闭开机自动启动。");
        }
        catch (SecurityException ex)
        {
            SetStatus($"无法修改开机启动设置：{ex.Message}");
            _settings.StartWithWindows = !enabled;
            _initializing = true;
            StartupCheck.IsChecked = !enabled;
            _initializing = false;
        }
        catch (UnauthorizedAccessException ex)
        {
            SetStatus($"无法修改开机启动设置：{ex.Message}");
            _settings.StartWithWindows = !enabled;
            _initializing = true;
            StartupCheck.IsChecked = !enabled;
            _initializing = false;
        }
        catch (IOException ex)
        {
            SetStatus($"保存开机启动设置失败：{ex.Message}");
            _settings.StartWithWindows = !enabled;
            _initializing = true;
            StartupCheck.IsChecked = !enabled;
            _initializing = false;
        }
    }

    private void TrySetStartup(bool enabled)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled);
        }
        catch (SecurityException ex)
        {
            SetStatus($"无法设置开机启动：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            SetStatus($"无法设置开机启动：{ex.Message}");
        }
        catch (IOException ex)
        {
            SetStatus($"无法设置开机启动：{ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus($"无法设置开机启动：{ex.Message}");
        }
    }

    private void CleanupExpiredEntries()
    {
        var cutoff = DateTime.Now.AddDays(-_settings.RetentionDays);
        var expired = _entries.Where(entry => entry.CreatedAt < cutoff).ToArray();
        foreach (var entry in expired)
        {
            _entries.Remove(entry);
            try
            {
                _store.DeleteImage(entry.ImagePath);
            }
            catch (IOException ex)
            {
                SetStatus($"清理过期图片失败：{ex.Message}");
            }
        }
        if (expired.Length > 0)
        {
            SaveEntries();
            UpdateEmptyMessage();
        }
        _lastCleanupUtc = DateTime.UtcNow;
    }

    private void ForegroundTimer_Tick(object? sender, EventArgs e)
    {
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != _windowHandle)
        {
            _lastExternalWindow = foreground;
        }

        if (DateTime.UtcNow - _lastCleanupUtc > TimeSpan.FromHours(1))
        {
            CleanupExpiredEntries();
        }
    }

    private void ConfigureTrayIcon()
    {
        _trayIcon.Text = "历史粘贴板";
        _trayIcon.Icon = System.Drawing.SystemIcons.Application;
        _trayIcon.Visible = true;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开历史粘贴板", null, (_, _) => ShowWindow());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _allowExit = true;
            Close();
        });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowExit) return;
        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _foregroundTimer.Stop();
        if (_windowHandle != IntPtr.Zero) RemoveClipboardFormatListener(_windowHandle);
        _source?.RemoveHook(WindowMessageHook);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipboardHistory");
        Directory.CreateDirectory(folder);
        var startInfo = new System.Diagnostics.ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(folder);
        try
        {
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            SetStatus($"无法打开保存位置：{ex.Message}");
        }
    }

    private void UpdateEmptyMessage()
    {
        if (EmptyMessage is null || _historyView is null) return;
        var anyMatches = _historyView.Cast<object>().Any();
        EmptyMessage.Text = _entries.Count == 0
            ? "还没有历史记录。复制一段文字或一张图片试试看。"
            : "没有找到匹配的记录。";
        EmptyMessage.Visibility = anyMatches ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetStatus(string message) => StatusText.Text = message;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
