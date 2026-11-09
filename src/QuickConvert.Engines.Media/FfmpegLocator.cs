using QuickConvert.Core.Diagnostics;

namespace QuickConvert.Engines.Media;

/// <summary>
/// Discovers and validates FFmpeg and FFprobe binary locations using a 3-tier hierarchy:
/// 1. %LOCALAPPDATA%\QuickConvert\tools\ffmpeg\&lt;version&gt;\bin
/// 2. &lt;appDirectory&gt;\tools\ffmpeg\bin
/// 3. System PATH environment variable
/// </summary>
public class FfmpegLocator
{
    private readonly string? _overrideFfmpegPath;
    private readonly string? _overrideFfprobePath;

    public FfmpegLocator(string? overrideFfmpegPath = null, string? overrideFfprobePath = null)
    {
        _overrideFfmpegPath = overrideFfmpegPath;
        _overrideFfprobePath = overrideFfprobePath;
    }

    /// <summary>
    /// Checks whether both ffmpeg.exe and ffprobe.exe are discoverable on the current machine.
    /// </summary>
    public bool IsFfmpegAvailable => !string.IsNullOrEmpty(LocateFfmpeg()) && !string.IsNullOrEmpty(LocateFfprobe());

    /// <summary>
    /// Resolves the absolute path to ffmpeg.exe, or null if not found.
    /// </summary>
    public virtual string? LocateFfmpeg()
    {
        if (!string.IsNullOrEmpty(_overrideFfmpegPath) && File.Exists(_overrideFfmpegPath))
        {
            return _overrideFfmpegPath;
        }

        return LocateBinary("ffmpeg.exe");
    }

    /// <summary>
    /// Resolves the absolute path to ffprobe.exe, or null if not found.
    /// </summary>
    public virtual string? LocateFfprobe()
    {
        if (!string.IsNullOrEmpty(_overrideFfprobePath) && File.Exists(_overrideFfprobePath))
        {
            return _overrideFfprobePath;
        }

        return LocateBinary("ffprobe.exe");
    }

    private static string? LocateBinary(string binaryName)
    {
        // 1. %LOCALAPPDATA%\QuickConvert\tools\ffmpeg\<version>\bin\<binaryName>
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            var userToolsBase = Path.Combine(localAppData, "QuickConvert", "tools", "ffmpeg");
            if (Directory.Exists(userToolsBase))
            {
                // Direct bin subfolder check
                var directBin = Path.Combine(userToolsBase, "bin", binaryName);
                if (File.Exists(directBin)) return directBin;

                // Versioned subdirectories e.g. tools\ffmpeg\9.0.2\bin\
                try
                {
                    var versionDirs = Directory.GetDirectories(userToolsBase);
                    foreach (var vDir in versionDirs.OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                    {
                        var candidate = Path.Combine(vDir, "bin", binaryName);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                catch { }
            }
        }

        // 2. <AppDirectory>\tools\ffmpeg\bin\<binaryName>
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var appToolsCandidate = Path.Combine(appDir, "tools", "ffmpeg", "bin", binaryName);
        if (File.Exists(appToolsCandidate)) return appToolsCandidate;

        // Dev environment root check (e.g. running from IDE / current working directory)
        var cwdToolsCandidate = Path.Combine(Directory.GetCurrentDirectory(), "tools", "ffmpeg", "bin", binaryName);
        if (File.Exists(cwdToolsCandidate)) return cwdToolsCandidate;

        // 3. System PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var segments = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                try
                {
                    var fullPath = Path.Combine(segment, binaryName);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch { }
            }
        }

        return null;
    }
}
