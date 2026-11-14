using Microsoft.Win32;
namespace ClipboardHistory;

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ClipboardHistory";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 开机启动设置。");

        if (!enabled)
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
            return;
        }

        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定软件启动路径。");
        var command = $"\"{processPath}\" --background";
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name
                ?? throw new InvalidOperationException("无法确定软件启动文件。");
            var entryAssemblyPath = Path.Combine(AppContext.BaseDirectory, $"{entryAssemblyName}.dll");
            command = $"\"{processPath}\" \"{entryAssemblyPath}\" --background";
        }
        key.SetValue(AppName, command);
    }
}
