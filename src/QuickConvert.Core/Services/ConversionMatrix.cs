namespace QuickConvert.Core.Services;

/// <summary>
/// Single source of truth for what may convert to what. Product scope lives here and only here;
/// FormatRegistry delegates to this table. Copied from PHASE_2_SPEC.md §3.
/// </summary>
public static class ConversionMatrix
{
    // source ext -> allowed target exts (empty = extraction/consumption only)
    private static readonly Dictionary<string, string[]> Matrix = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- images -------------------------------------------------------------
        [".jpg"]  = [".png", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".jpeg"] = [".png", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".png"]  = [".jpg", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".webp"] = [".jpg", ".png", ".bmp", ".tiff", ".gif", ".pdf"],
        [".bmp"]  = [".jpg", ".png", ".webp", ".tiff", ".gif", ".pdf"],
        [".tiff"] = [".jpg", ".png", ".webp", ".bmp", ".gif", ".pdf"],
        [".gif"]  = [".jpg", ".png", ".webp", ".bmp", ".tiff", ".pdf"],

        // --- audio (Phase 3) ----------------------------------------------------
        [".mp3"] = [".wav", ".flac", ".aac", ".ogg", ".m4a"],
        [".wav"] = [".mp3", ".flac", ".aac", ".ogg", ".m4a"],
        [".flac"] = [".mp3", ".wav", ".aac", ".ogg", ".m4a"],
        [".aac"] = [".mp3", ".wav", ".flac", ".ogg", ".m4a"],
        [".ogg"] = [".mp3", ".wav", ".flac", ".aac", ".m4a"],
        [".m4a"] = [".mp3", ".wav", ".flac", ".aac", ".ogg"],
        [".wma"] = [".mp3", ".wav", ".flac"],

        // --- video (Phase 3) ----------------------------------------------------
        [".mp4"]  = [".mkv", ".webm", ".mov", ".gif", ".mp3", ".wav", ".m4a"],
        [".mkv"]  = [".mp4", ".webm", ".mov", ".gif", ".mp3", ".wav", ".m4a"],
        [".avi"]  = [".mp4", ".mkv", ".webm", ".mov", ".gif", ".mp3", ".wav"],
        [".mov"]  = [".mp4", ".mkv", ".webm", ".gif", ".mp3", ".wav"],
        [".webm"] = [".mp4", ".mkv", ".mov", ".gif", ".mp3", ".wav"],
        [".wmv"]  = [".mp4", ".mkv", ".gif", ".mp3", ".wav"],

        // --- documents (Phase 4) ------------------------------------------------
        [".txt"]  = [".md", ".html", ".pdf", ".docx", ".csv"],
        [".md"]   = [".txt", ".html", ".pdf", ".docx"],
        [".html"] = [".txt", ".md", ".pdf", ".docx"],
        [".csv"]  = [".json", ".txt", ".xlsx", ".md"],
        [".json"] = [".csv", ".txt", ".yaml"],
        [".docx"] = [".pdf", ".txt", ".md", ".html", ".rtf", ".odt"],
        [".rtf"]  = [".txt", ".html", ".pdf", ".docx"],
        [".odt"]  = [".txt", ".html", ".pdf", ".docx"],
        [".pdf"]  = [".txt", ".png", ".jpg"],   // text extraction / first page only

        // --- archives (Phase 4) -------------------------------------------------
        [".zip"] = [".7z", ".tar", ".gz"],
        [".7z"]  = [".zip", ".tar"],
        [".tar"] = [".zip", ".7z", ".gz"],
        [".gz"]  = [".zip", ".7z", ".tar"],
        [".rar"] = [".zip", ".7z", ".tar"],     // extract-only source. NEVER a target.
    };

    /// <summary>All source extensions the matrix knows about (deterministic order, for enumeration/tests/mocks).</summary>
    public static IReadOnlyList<string> Sources { get; } =
        Matrix.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsKnownSource(string ext) => Matrix.ContainsKey(Normalize(ext));

    public static IReadOnlyList<string> GetTargets(string sourceExt) =>
        Matrix.TryGetValue(Normalize(sourceExt), out var t) ? t : [];

    public static bool IsAllowed(string sourceExt, string targetExt)
    {
        var target = Normalize(targetExt);
        return GetTargets(sourceExt).Contains(target, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Extensions this matrix must never offer as output (licence/feasibility).</summary>
    public static IReadOnlySet<string> ForbiddenTargets { get; } =
        new HashSet<string>([".rar"], StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string? ext) =>
        string.IsNullOrWhiteSpace(ext) ? string.Empty
        : ext.Trim().ToLowerInvariant().StartsWith('.') ? ext.Trim().ToLowerInvariant()
        : "." + ext.Trim().ToLowerInvariant();
}
