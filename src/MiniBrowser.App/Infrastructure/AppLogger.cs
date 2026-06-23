using System.IO;

namespace MiniBrowser.App.Infrastructure;

public static class AppLogger
{
    private static readonly object Sync = new();

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(Exception exception, string message)
    {
        Write("ERROR", message + Environment.NewLine + exception);
    }

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(Path.Combine(RuntimePaths.LogsDirectory, "MiniBrowser.log"), line);
            }
        }
        catch
        {
            // Logging must never break the browser.
        }
    }
}
