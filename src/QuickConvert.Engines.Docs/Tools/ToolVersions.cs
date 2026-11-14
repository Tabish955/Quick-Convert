namespace QuickConvert.Engines.Docs.Tools;

/// <summary>Pinned versions. Must match the scripts under tools/Get-*.ps1.</summary>
public static class ToolVersions
{
    public const string Pandoc = "3.5";
    public const string Wkhtmltopdf = "0.12.6-1";
    public const string SevenZip = "23.01";

    public static ToolLocator PandocLocator() => new("pandoc", "pandoc.exe", Pandoc);
    public static ToolLocator WkhtmltopdfLocator() => new("wkhtmltopdf", "wkhtmltopdf.exe", Wkhtmltopdf);
    public static ToolLocator SevenZipLocator() => new("7z", "7za.exe", SevenZip);
}
