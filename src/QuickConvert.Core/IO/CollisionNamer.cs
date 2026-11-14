namespace QuickConvert.Core.IO;

/// <summary>The "photo (1).png" rule, for files, folders, and entries inside one archive.</summary>
public static class CollisionNamer
{
    public static string NextAvailableFile(string path, Func<string, bool> exists)
    {
        if (!exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!exists(candidate)) return candidate;
        }
    }

    /// <summary>Folders never have an extension: "archive.v2" becomes "archive.v2 (1)".</summary>
    public static string NextAvailableDirectory(string path, Func<string, bool> exists)
    {
        if (!exists(path)) return path;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var n = 1; ; n++)
        {
            var candidate = $"{trimmed} ({n})";
            if (!exists(candidate)) return candidate;
        }
    }
}

public sealed class ArchiveStructureException(string message) : Exception(message);

/// <summary>
/// Registry of relative paths already used while extracting one archive. Case-insensitive (Windows).
/// Two entries wanting the same name get "name (1).ext" in the same folder; structure is preserved.
/// </summary>
public sealed class RelativePathSet
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);

    public string ReserveFile(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        var dir = slash < 0 ? string.Empty : relativePath[..slash];
        var file = slash < 0 ? relativePath : relativePath[(slash + 1)..];

        ReserveDirectory(dir);

        var name = Path.GetFileNameWithoutExtension(file);
        var ext = Path.GetExtension(file);
        var candidate = file;
        for (var n = 1; Taken(Join(dir, candidate)); n++)
            candidate = $"{name} ({n}){ext}";

        var result = Join(dir, candidate);
        _files.Add(result);
        return result;
    }

    public void ReserveDirectory(string relativeDir)
    {
        if (relativeDir.Length == 0) return;
        var acc = string.Empty;
        foreach (var part in relativeDir.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            acc = Join(acc, part);
            if (_files.Contains(acc))
                throw new ArchiveStructureException($"The archive contains both a file and a folder named '{acc}'.");
            _dirs.Add(acc);
        }
    }

    private bool Taken(string p) => _files.Contains(p) || _dirs.Contains(p);
    private static string Join(string a, string b) => a.Length == 0 ? b : a + "/" + b;
}
