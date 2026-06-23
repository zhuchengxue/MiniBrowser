using System.IO;

namespace MiniBrowser.App.Infrastructure;

public static class RuntimePaths
{
    public static string AppDirectory => AppContext.BaseDirectory;
    public static string DataDirectory => EnsureDirectory(Path.Combine(AppDirectory, "Data"));
    public static string WebView2DataDirectory => EnsureDirectory(Path.Combine(DataDirectory, "WebView2"));
    public static string LogsDirectory => EnsureDirectory(Path.Combine(DataDirectory, "Logs"));
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string SettingsBackupPath => Path.Combine(DataDirectory, "settings.backup.json");
    public static string EasyListLitePath => Path.Combine(AppDirectory, "adblock", "easylist-lite.txt");
    public static string AppIconPath => Path.Combine(AppDirectory, "Assets", "App.ico");

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
