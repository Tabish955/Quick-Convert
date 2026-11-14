using QuickConvert.Core.Models;

namespace QuickConvert.Core.Services;

/// <summary>
/// Lookup helpers over ConversionMatrix. Product scope lives in ConversionMatrix;
/// this class only normalizes extensions, maps categories and computes intersections.
/// </summary>
public static class FormatRegistry
{
    public static readonly IReadOnlyList<string> ImageFormats = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tiff", ".gif"];
    public static readonly IReadOnlyList<string> AudioFormats = [".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma"];
    public static readonly IReadOnlyList<string> VideoFormats = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv"];
    public static readonly IReadOnlyList<string> DocumentFormats = [".txt", ".md", ".html", ".docx", ".rtf", ".odt", ".pdf"];
    public static readonly IReadOnlyList<string> SpreadsheetFormats = [".csv", ".json"];
    public static readonly IReadOnlyList<string> ArchiveFormats = [".zip", ".7z", ".tar", ".gz", ".rar"];
    public static readonly IReadOnlyList<string> EBookFormats = [];

    private static readonly Dictionary<string, FileCategory> SourceCategories = BuildCategoryMap();

    private static Dictionary<string, FileCategory> BuildCategoryMap()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<string> exts, FileCategory cat) { foreach (var e in exts) map[e] = cat; }

        Add(ImageFormats, FileCategory.Image);
        Add(AudioFormats, FileCategory.Audio);
        Add(VideoFormats, FileCategory.Video);
        Add(DocumentFormats, FileCategory.Document);
        Add(SpreadsheetFormats, FileCategory.Spreadsheet);
        Add(ArchiveFormats, FileCategory.Archive);
        return map;
    }

    /// <summary>
    /// Normalizes extension string to lowercase with a leading dot.
    /// Returns string.Empty if invalid. Reserved tokens (starting with #) and &lt;dir&gt; are preserved.
    /// </summary>
    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
        var trimmed = extension.Trim().ToLowerInvariant();
        if (ConversionTargets.IsReserved(trimmed) || trimmed == "<dir>") return trimmed;
        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }

    /// <summary>
    /// Determines the FileCategory for a given file extension or full file path.
    /// Only extensions that are conversion sources in the matrix get a category.
    /// </summary>
    public static FileCategory GetCategory(string extensionOrPath)
    {
        var ext = Path.HasExtension(extensionOrPath)
            ? Path.GetExtension(extensionOrPath)
            : extensionOrPath;

        var normalized = NormalizeExtension(ext);
        return SourceCategories.TryGetValue(normalized, out var category)
            ? category
            : FileCategory.Unknown;
    }

    /// <summary>
    /// Checks whether an extension is a known conversion source in the matrix.
    /// </summary>
    public static bool IsSupported(string extensionOrPath)
    {
        var normalized = NormalizeExtension(Path.HasExtension(extensionOrPath) ? Path.GetExtension(extensionOrPath) : extensionOrPath);
        return ConversionMatrix.IsKnownSource(normalized);
    }

    /// <summary>
    /// Returns the complete list of target extensions compatible with the given source extension.
    /// Straight from the matrix: targets never include the source itself.
    /// </summary>
    public static IReadOnlyList<string> GetSupportedTargetFormats(string sourceExtension) =>
        ConversionMatrix.GetTargets(NormalizeExtension(sourceExtension));

    /// <summary>
    /// When multiple files are selected in Explorer, returns target formats common to ALL selected files.
    /// Preserves the ORDER of the first file's target list so the pre-selected default is deterministic.
    /// </summary>
    public static IReadOnlyList<string> GetCommonTargetFormats(IEnumerable<string> inputFilePaths)
    {
        var paths = inputFilePaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (paths == null || paths.Count == 0) return [];

        var orderedTargets = GetSupportedTargetFormats(Path.GetExtension(paths[0]));
        if (orderedTargets.Count == 0) return [];

        var commonTargets = new HashSet<string>(orderedTargets, StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < paths.Count && commonTargets.Count > 0; i++)
        {
            commonTargets.IntersectWith(GetSupportedTargetFormats(Path.GetExtension(paths[i])));
        }

        return orderedTargets.Where(commonTargets.Contains).ToList();
    }
}
