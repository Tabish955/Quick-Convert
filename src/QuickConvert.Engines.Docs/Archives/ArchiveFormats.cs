namespace QuickConvert.Engines.Docs.Archives;

public static class ArchiveFormats
{
    /// <summary>Everything that can be extracted. RAR is read-only.</summary>
    public static readonly IReadOnlySet<string> ExtractableSources =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".tar", ".gz", ".rar" };

    /// <summary>Everything that can be created. RAR is never here (unRAR licence).</summary>
    public static readonly IReadOnlySet<string> CompressionTargets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".tar", ".gz" };

    public static bool IsExtractable(string ext) => ExtractableSources.Contains(ext);
    public static bool IsValidTarget(string ext) => CompressionTargets.Contains(ext);
}
