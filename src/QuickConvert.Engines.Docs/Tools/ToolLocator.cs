namespace QuickConvert.Engines.Docs.Tools;

/// <summary>
/// Three-tier lookup, same order as FfmpegLocator:
/// 1) %LOCALAPPDATA%\QuickConvert\tools\&lt;folder&gt;\&lt;version&gt;\&lt;exe&gt;
/// 2) &lt;app dir&gt;\tools\&lt;folder&gt;\&lt;exe&gt; or repository working directory
/// 3) PATH. Never downloads anything.
/// </summary>
public sealed class ToolLocator
{
    private readonly string _folder, _exe, _version;
    private readonly Func<string, bool> _fileExists;
    private readonly string? _localAppData, _appBase, _pathEnv;

    public ToolLocator(string folder, string exe, string version,
        Func<string, bool>? fileExists = null, string? localAppData = null, string? appBase = null, string? pathEnv = null)
    {
        _folder = folder; _exe = exe; _version = version;
        _fileExists = fileExists ?? File.Exists;
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _appBase = appBase ?? AppContext.BaseDirectory;
        _pathEnv = pathEnv ?? Environment.GetEnvironmentVariable("PATH");
    }

    public string? Locate()
    {
        if (!string.IsNullOrEmpty(_localAppData))
        {
            var p = Path.Combine(_localAppData, "QuickConvert", "tools", _folder, _version, _exe);
            if (_fileExists(p)) return p;
        }

        if (!string.IsNullOrEmpty(_appBase))
        {
            var p = Path.Combine(_appBase, "tools", _folder, _exe);
            if (_fileExists(p)) return p;
        }

        var cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrEmpty(cwd))
        {
            var p = Path.Combine(cwd, "tools", _folder, _exe);
            if (_fileExists(p)) return p;
        }

        if (!string.IsNullOrEmpty(_pathEnv))
        {
            foreach (var dir in _pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var p = Path.Combine(dir.Trim().Trim('"'), _exe);
                    if (_fileExists(p)) return p;
                }
                catch (ArgumentException) { /* malformed PATH entry */ }
            }
        }
        return null;
    }
}
