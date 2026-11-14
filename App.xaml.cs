using System.Windows;

namespace ClipboardHistory;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            window.ShowInTaskbar = false;
            window.Show();
            window.Hide();
        }
        else
        {
            window.Show();
        }
    }
}
