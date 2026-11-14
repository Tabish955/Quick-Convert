namespace QuickConvert.Engines.Docs.Tools;

/// <summary>Engine-neutral outcome. The thin IConversionEngine adapters translate this to ConversionResult.</summary>
public sealed record ToolResult(bool Success, string? OutputPath, string Message)
{
    public static ToolResult Ok(string path) => new(true, path, string.Empty);
    public static ToolResult Fail(string message) => new(false, null, message);
}

internal static class TempCleanup
{
    public static void Purge(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best effort; a locked temp must never mask the real result */ }
    }
}
