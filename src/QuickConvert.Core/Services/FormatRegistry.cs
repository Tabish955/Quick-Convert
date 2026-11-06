using QuickConvert.Core.Models;

namespace QuickConvert.Core.Services;

/// <summary>
/// Convertio format matrix and extension mapping registry.
/// Provides lookup for file categories, supported targets, and common multi-file targets.
/// </summary>
public static class FormatRegistry
{
    private static readonly Dictionary<string, FileCategory> ExtensionCategoryMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<string>> CustomTargetMap = new(StringComparer.OrdinalIgnoreCase);

    // Standard format lists per category
    public static readonly IReadOnlyList<string> ImageFormats = new[]
    {
        ".png", ".jpg", ".jpeg", ".webp", ".avif", ".heic", ".svg", ".ico", ".bmp", ".tiff", ".gif", ".psd", ".cr2", ".nef"
    };

    public static readonly IReadOnlyList<string> VideoFormats = new[]
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".flv", ".wmv", ".m4v", ".gif"
    };

    public static readonly IReadOnlyList<string> AudioFormats = new[]
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus"
    };

    public static readonly IReadOnlyList<string> DocumentFormats = new[]
    {
        ".docx", ".pdf", ".txt", ".md", ".html", ".epub", ".rtf", ".odt", ".tex"
    };

    public static readonly IReadOnlyList<string> SpreadsheetFormats = new[]
    {
        ".xlsx", ".xls", ".csv", ".json", ".xml", ".yaml"
    };

    public static readonly IReadOnlyList<string> ArchiveFormats = new[]
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz"
    };

    public static readonly IReadOnlyList<string> EBookFormats = new[]
    {
        ".epub", ".mobi", ".azw3", ".pdf", ".txt", ".fb2"
    };

    public static readonly IReadOnlyList<string> FontFormats = new[]
    {
        ".ttf", ".otf", ".woff", ".woff2", ".eot"
    };

    static FormatRegistry()
    {
        // Populate category mappings
        foreach (var ext in ImageFormats) ExtensionCategoryMap[ext] = FileCategory.Image;
        foreach (var ext in VideoFormats) ExtensionCategoryMap[ext] = FileCategory.Video;
        foreach (var ext in AudioFormats) ExtensionCategoryMap[ext] = FileCategory.Audio;
        foreach (var ext in DocumentFormats) ExtensionCategoryMap[ext] = FileCategory.Document;
        foreach (var ext in SpreadsheetFormats) ExtensionCategoryMap[ext] = FileCategory.Spreadsheet;
        foreach (var ext in ArchiveFormats) ExtensionCategoryMap[ext] = FileCategory.Archive;
        foreach (var ext in EBookFormats)
        {
            if (!ExtensionCategoryMap.ContainsKey(ext))
                ExtensionCategoryMap[ext] = FileCategory.EBook;
        }
        foreach (var ext in FontFormats) ExtensionCategoryMap[ext] = FileCategory.Font;
    }

    /// <summary>
    /// Normalizes extension string to lowercase with a leading dot.
    /// Returns string.Empty if invalid.
    /// </summary>
    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
        var trimmed = extension.Trim().ToLowerInvariant();
        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }

    /// <summary>
    /// Determines the FileCategory for a given file extension or full file path.
    /// </summary>
    public static FileCategory GetCategory(string extensionOrPath)
    {
        var ext = Path.HasExtension(extensionOrPath)
            ? Path.GetExtension(extensionOrPath)
            : extensionOrPath;

        var normalized = NormalizeExtension(ext);
        return ExtensionCategoryMap.TryGetValue(normalized, out var category)
            ? category
            : FileCategory.Unknown;
    }

    /// <summary>
    /// Checks whether an extension is known to the registry.
    /// </summary>
    public static bool IsSupported(string extensionOrPath)
    {
        var normalized = NormalizeExtension(Path.HasExtension(extensionOrPath) ? Path.GetExtension(extensionOrPath) : extensionOrPath);
        return ExtensionCategoryMap.ContainsKey(normalized);
    }

    /// <summary>
    /// Returns the complete list of target extensions compatible with the given source extension.
    /// </summary>
    public static IReadOnlyList<string> GetSupportedTargetFormats(string sourceExtension)
    {
        var src = NormalizeExtension(sourceExtension);
        if (string.IsNullOrEmpty(src)) return Array.Empty<string>();

        // Check if there is an explicit override/custom map
        if (CustomTargetMap.TryGetValue(src, out var customList))
        {
            return customList;
        }

        var category = GetCategory(src);
        var targets = new List<string>();

        switch (category)
        {
            case FileCategory.Image:
                // Primary targets for images + PDF
                string[] imageTargets = [".png", ".jpg", ".jpeg", ".webp", ".avif", ".ico", ".bmp", ".tiff", ".gif", ".pdf", ".svg"];
                foreach (var t in imageTargets)
                {
                    if (!t.Equals(src, StringComparison.OrdinalIgnoreCase))
                        targets.Add(t);
                }
                break;

            case FileCategory.Video:
                // Video to other video containers
                string[] videoTargets = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".gif"];
                foreach (var t in videoTargets)
                {
                    if (!t.Equals(src, StringComparison.OrdinalIgnoreCase))
                        targets.Add(t);
                }
                // Video to audio extraction
                string[] videoToAudio = [".mp3", ".wav", ".aac", ".flac", ".m4a", ".ogg"];
                targets.AddRange(videoToAudio);
                break;

            case FileCategory.Audio:
                string[] audioTargets = [".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus"];
                foreach (var t in audioTargets)
                {
                    if (!t.Equals(src, StringComparison.OrdinalIgnoreCase))
                        targets.Add(t);
                }
                break;

            case FileCategory.Document:
                string[] docTargets = [".pdf", ".docx", ".txt", ".md", ".html", ".epub", ".rtf", ".odt", ".tex"];
                foreach (var t in docTargets)
                {
                    if (!t.Equals(src, StringComparison.OrdinalIgnoreCase))
                        targets.Add(t);
                }
                break;

            case FileCategory.Spreadsheet:
                string[] sheetTargets = [".xlsx", ".csv", ".json", ".xml", ".yaml", ".xls", ".pdf", ".html"];
                foreach (var t in sheetTargets)
                {
                    if (!t.Equals(src, StringComparison.OrdinalIgnoreCase))
                        targets.Add(t);
                }
                break;

            case FileCategory.Archive:
                string[] archiveTargets = [".zip", ".7z", ".tar", ".gz", ".bz2", ".xz"];
                foreach (var t in archiveTargets)
                {
                    if (!t.Equals(src, StringComparison.OrdinalIgnoreCase))
                        targets.Add(t);
                }
                break;

            case FileCategory.EBook:
                string[] ebookTargets = [".epub", ".pdf", ".mobi", ".azw3", ".txt", ".fb2"];
                foreach (var t in ebookTargets)
                {
                    if (!t.Equals(src, StringComparison.OrdinalIgnoreCase))
                        targets.Add(t);
                }
                break;

            case FileCategory.Font:
                string[] fontTargets = [".ttf", ".otf", ".woff", ".woff2", ".eot"];
                foreach (var t in fontTargets)
                {
                    if (!t.Equals(src, StringComparison.OrdinalIgnoreCase))
                        targets.Add(t);
                }
                break;

            default:
                break;
        }

        return targets;
    }

    /// <summary>
    /// When multiple files are selected in Explorer, returns target formats common to ALL selected files.
    /// For instance, if 10 images are selected, returns formats they can all convert to (PNG, JPG, WEBP, PDF, etc.).
    /// </summary>
    public static IReadOnlyList<string> GetCommonTargetFormats(IEnumerable<string> inputFilePaths)
    {
        var paths = inputFilePaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (paths == null || paths.Count == 0) return Array.Empty<string>();

        // Preserve the ORDER of the first file's target list so that index 0 (the format the UI
        // pre-selects) is deterministic. A HashSet would make the default format vary between runs
        // for the same multi-file selection.
        var orderedTargets = GetSupportedTargetFormats(Path.GetExtension(paths[0]));
        if (orderedTargets.Count == 0) return Array.Empty<string>();

        var commonTargets = new HashSet<string>(orderedTargets, StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < paths.Count && commonTargets.Count > 0; i++)
        {
            commonTargets.IntersectWith(GetSupportedTargetFormats(Path.GetExtension(paths[i])));
        }

        return orderedTargets.Where(commonTargets.Contains).ToList();
    }
}
