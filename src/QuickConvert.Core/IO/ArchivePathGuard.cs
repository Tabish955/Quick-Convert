namespace QuickConvert.Core.IO;

public enum ArchivePathError { None, Empty, Rooted, DriveLetter, Escapes, InvalidCharacters, ReservedName }

public readonly record struct ArchivePathResult(bool IsValid, string RelativePath, ArchivePathError Error, string Message);

/// <summary>
/// Pure zip-slip guard. Every archive entry name goes through <see cref="Validate"/> before any
/// path is built, and <see cref="Combine"/> re-checks containment after the OS has normalised the path.
/// </summary>
public static class ArchivePathGuard
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON","PRN","AUX","NUL",
        "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    public static ArchivePathResult Validate(string? entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
            return Fail(ArchivePathError.Empty, "an entry has an empty name");

        if (entryKey.IndexOf('\0') >= 0)
            return Fail(ArchivePathError.InvalidCharacters, $"entry '{Printable(entryKey)}' contains a NUL character");

        var p = entryKey.Replace('\\', '/');

        if (p.StartsWith('/'))
            return Fail(ArchivePathError.Rooted, $"entry '{entryKey}' has an absolute path");

        if (p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':')
            return Fail(ArchivePathError.DriveLetter, $"entry '{entryKey}' has a drive letter");

        // Any other ':' would be an NTFS alternate data stream or a malformed name.
        if (p.Contains(':'))
            return Fail(ArchivePathError.InvalidCharacters, $"entry '{entryKey}' contains ':'");

        var stack = new List<string>();
        foreach (var seg in p.Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;

            if (seg == "..")
            {
                if (stack.Count == 0)
                    return Fail(ArchivePathError.Escapes, $"entry '{entryKey}' escapes the destination folder");
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            if (seg.IndexOfAny(InvalidNameChars) >= 0)
                return Fail(ArchivePathError.InvalidCharacters, $"entry '{entryKey}' contains characters Windows does not allow");

            if (seg.EndsWith('.') || seg.EndsWith(' '))
                return Fail(ArchivePathError.InvalidCharacters, $"entry '{entryKey}' has a name ending in a dot or space");

            var stem = seg.Split('.')[0];
            if (ReservedNames.Contains(stem))
                return Fail(ArchivePathError.ReservedName, $"entry '{entryKey}' uses a reserved device name");

            stack.Add(seg);
        }

        if (stack.Count == 0)
            return Fail(ArchivePathError.Empty, $"entry '{entryKey}' has no usable name");

        return new ArchivePathResult(true, string.Join('/', stack), ArchivePathError.None, string.Empty);
    }

    /// <summary>Builds the absolute path and verifies it is still inside <paramref name="root"/>.</summary>
    public static string Combine(string root, string relativePath)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path '{relativePath}' resolves outside the destination folder.");
        return full;
    }

    private static string Printable(string s) => s.Replace("\0", "\\0");
    private static ArchivePathResult Fail(ArchivePathError e, string m) => new(false, string.Empty, e, m);
}
