using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;

namespace QuickConvert.Engines.Media;

/// <summary>
/// Placeholder engine stub for Media conversions (FFmpeg integration).
/// TODO: Phase 3 - Implement production FFmpeg wrapper for Video and Audio transcoding,
/// hardware acceleration detection (NVENC/QSV/AMF), and audio extraction.
/// </summary>
public class MediaEngineStub : IConversionEngine
{
    public string EngineName => "MediaEngine (FFmpeg - Phase 3 Stub)";

    public IReadOnlyList<string> SupportedSourceExtensions =>
    [
        .. FormatRegistry.VideoFormats,
        .. FormatRegistry.AudioFormats
    ];

    public bool CanConvert(string sourceExt, string targetExt)
    {
        var srcCat = FormatRegistry.GetCategory(sourceExt);
        return srcCat == FileCategory.Video || srcCat == FileCategory.Audio;
    }

    public Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        ConversionOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        // TODO: Phase 3 - Wire real FFmpeg binary located in /tools/ffmpeg/
        throw new NotImplementedException("MediaEngine is slated for implementation in Phase 3.");
    }
}
