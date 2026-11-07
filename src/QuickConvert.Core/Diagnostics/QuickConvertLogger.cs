namespace QuickConvert.Core.Diagnostics;

/// <summary>
/// Rolling file log at %LOCALAPPDATA%\QuickConvert\logs, 7-day retention.
/// No package dependency, offline-first, and it can never take the app down:
/// every operation is wrapped and swallowed on failure.
///
/// Privacy contract: Info level carries no file paths; Debug level may carry full detail.
/// </summary>
public static class QuickConvertLogger
{
    private static readonly object Gate = new();

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickConvert", "logs");

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    /// <summary>Deletes log files older than the retention window. Called once at startup.</summary>
    public static void PurgeOldLogs(int retentionDays = 7)
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            foreach (var f in Directory.GetFiles(LogDirectory, "quickconvert-*.log"))
            {
                if (File.GetCreationTime(f) < DateTime.Now.AddDays(-retentionDays))
                {
                    File.Delete(f);
                }
            }
        }
        catch { /* logging must never take the app down */ }
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(LogDirectory, $"quickconvert-{DateTime.Now:yyyyMMdd}.log"), line);
            }
        }
        catch { /* a failed log write is not worth crashing over */ }
    }
}
