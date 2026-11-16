using QuickConvert.Core.Diagnostics;

namespace QuickConvert.Core.Tools;

/// <summary>
/// Startup tool probe that checks availability of external conversion binaries:
/// FFmpeg (ffmpeg.exe), Pandoc (pandoc.exe), wkhtmltopdf (wkhtmltopdf.exe), and 7-Zip (7za.exe).
/// Designed for non-blocking background execution during application startup.
/// </summary>
public sealed class ToolProbeService : IToolProbeService
{
    private readonly Func<bool> _isFfmpegAvailable;
    private readonly Func<bool> _isPandocAvailable;
    private readonly Func<bool> _isWkhtmltopdfAvailable;
    private readonly Func<bool> _isSevenZipAvailable;

    public ToolProbeService(
        Func<bool>? isFfmpegAvailable = null,
        Func<bool>? isPandocAvailable = null,
        Func<bool>? isWkhtmltopdfAvailable = null,
        Func<bool>? isSevenZipAvailable = null)
    {
        _isFfmpegAvailable = isFfmpegAvailable ?? (() => ProbeBinary("ffmpeg", "ffmpeg.exe", inBinSubdir: true));
        _isPandocAvailable = isPandocAvailable ?? (() => ProbeBinary("pandoc", "pandoc.exe"));
        _isWkhtmltopdfAvailable = isWkhtmltopdfAvailable ?? (() => ProbeBinary("wkhtmltopdf", "wkhtmltopdf.exe"));
        _isSevenZipAvailable = isSevenZipAvailable ?? (() => ProbeBinary("7z", "7za.exe"));
    }

    public static bool ProbeBinary(string toolSubfolder, string exeName, bool inBinSubdir = false)
    {
        try
        {
            // 1. %LOCALAPPDATA%\QuickConvert\tools\<toolSubfolder>\...
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                var baseDir = Path.Combine(localAppData, "QuickConvert", "tools", toolSubfolder);
                if (Directory.Exists(baseDir))
                {
                    var candidate = inBinSubdir ? Path.Combine(baseDir, "bin", exeName) : Path.Combine(baseDir, exeName);
                    if (File.Exists(candidate)) return true;

                    try
                    {
                        foreach (var sub in Directory.GetDirectories(baseDir))
                        {
                            var vCand = inBinSubdir ? Path.Combine(sub, "bin", exeName) : Path.Combine(sub, exeName);
                            if (File.Exists(vCand)) return true;
                        }
                    }
                    catch { }
                }
            }

            // 2. App base directory or parent folders (e.g. repo root)
            var appBase = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(appBase))
            {
                var direct = inBinSubdir
                    ? Path.Combine(appBase, "tools", toolSubfolder, "bin", exeName)
                    : Path.Combine(appBase, "tools", toolSubfolder, exeName);
                if (File.Exists(direct)) return true;

                try
                {
                    var dir = new DirectoryInfo(appBase);
                    while (dir.Parent != null)
                    {
                        dir = dir.Parent;
                        var parentCandidate = inBinSubdir
                            ? Path.Combine(dir.FullName, "tools", toolSubfolder, "bin", exeName)
                            : Path.Combine(dir.FullName, "tools", toolSubfolder, exeName);
                        if (File.Exists(parentCandidate)) return true;
                    }
                }
                catch { }
            }

            // 3. Current working directory
            var cwd = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(cwd))
            {
                var cwdCandidate = inBinSubdir
                    ? Path.Combine(cwd, "tools", toolSubfolder, "bin", exeName)
                    : Path.Combine(cwd, "tools", toolSubfolder, exeName);
                if (File.Exists(cwdCandidate)) return true;
            }

            // 4. System PATH environment variable
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        var p = Path.Combine(segment.Trim().Trim('"'), exeName);
                        if (File.Exists(p)) return true;
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Failed while probing for binary '{exeName}': {ex.Message}");
        }

        return false;
    }

    public ToolProbeResult Probe()
    {
        var missing = new List<MissingToolInfo>();

        try
        {
            if (!_isFfmpegAvailable())
            {
                missing.Add(new MissingToolInfo(
                    "FFmpeg",
                    "ffmpeg.exe",
                    @"tools\Get-Ffmpeg.ps1",
                    "Audio and video conversions (mp4, mkv, avi, mov, webm, mp3, wav, flac, etc.)"));
            }

            if (!_isPandocAvailable())
            {
                missing.Add(new MissingToolInfo(
                    "Pandoc",
                    "pandoc.exe",
                    @"tools\Get-Pandoc.ps1",
                    "Document conversions (docx, md, rtf, txt, odt, html)"));
            }

            if (!_isWkhtmltopdfAvailable())
            {
                missing.Add(new MissingToolInfo(
                    "wkhtmltopdf",
                    "wkhtmltopdf.exe",
                    @"tools\Get-Wkhtmltopdf.ps1",
                    "PDF output generation from documents and HTML"));
            }

            if (!_isSevenZipAvailable())
            {
                missing.Add(new MissingToolInfo(
                    "7-Zip",
                    "7za.exe",
                    @"tools\Get-7za.ps1",
                    "Creating .7z compressed archives"));
            }
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Unexpected exception during tool probe: {ex.Message}");
        }

        return new ToolProbeResult(missing.Count == 0, missing);
    }

    public Task<ToolProbeResult> ProbeAsync()
    {
        return Task.Run(Probe);
    }
}
