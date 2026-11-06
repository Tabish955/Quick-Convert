namespace QuickConvert.Core.Models;

/// <summary>
/// User-configurable options governing the conversion execution.
/// </summary>
public record ConversionOptions
{
    /// <summary>
    /// Target file extension including leading dot (e.g., ".png", ".mp4").
    /// </summary>
    public string TargetExtension { get; init; } = string.Empty;

    /// <summary>
    /// Target directory for output file. When null or empty, uses the input file's directory.
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>
    /// Quality factor between 1 and 100 (defaults to 85).
    /// </summary>
    public int Quality { get; init; } = 85;

    /// <summary>
    /// Whether existing destination files should be overwritten.
    /// </summary>
    public bool OverwriteExisting { get; init; } = false;
}
