namespace QuickConvert.Engines.Media;

/// <summary>
/// Service that discovers and probes FFmpeg capabilities, supported encoders, and tests hardware acceleration.
/// </summary>
public interface IFfmpegProbeService
{
    /// <summary>
    /// Gets the cached capabilities, probing once synchronously if not yet evaluated.
    /// </summary>
    FfmpegCapabilities GetCapabilities();

    /// <summary>
    /// Gets or probes the capabilities asynchronously, caching the result for the process lifetime.
    /// </summary>
    Task<FfmpegCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);
}
