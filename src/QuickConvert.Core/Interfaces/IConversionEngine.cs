using QuickConvert.Core.Models;

namespace QuickConvert.Core.Interfaces;

/// <summary>
/// Core contract for conversion engine implementations (FFmpeg, Pandoc, 7-Zip, ImageMagick, etc.).
/// </summary>
public interface IConversionEngine
{
    /// <summary>
    /// Descriptive name of the conversion engine.
    /// </summary>
    string EngineName { get; }

    /// <summary>
    /// Source extensions supported by this engine.
    /// </summary>
    IReadOnlyList<string> SupportedSourceExtensions { get; }

    /// <summary>
    /// Returns true if this engine is capable of converting between the source and target extensions.
    /// </summary>
    bool CanConvert(string sourceExt, string targetExt);

    /// <summary>
    /// Executes the conversion asynchronously, reporting progress and honoring cancellation.
    /// </summary>
    Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        ConversionOptions options,
        IProgress<double>? progress,
        CancellationToken ct);
}
