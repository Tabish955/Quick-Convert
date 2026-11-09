using System.Text.RegularExpressions;
using QuickConvert.Core.Diagnostics;

namespace QuickConvert.Engines.Media;

/// <summary>
/// Discovers and caches FFmpeg capabilities, available encoders, and hardware acceleration support.
/// </summary>
public class FfmpegProbeService : IFfmpegProbeService
{
    private static readonly string[] CandidateHardwareEncoders =
    [
        "h264_nvenc",
        "hevc_nvenc",
        "h264_qsv",
        "hevc_qsv",
        "h264_amf",
        "hevc_amf"
    ];

    private readonly IFfmpegProcessRunner _runner;
    private readonly FfmpegLocator _locator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FfmpegCapabilities? _cached;

    public FfmpegProbeService(IFfmpegProcessRunner? runner = null, FfmpegLocator? locator = null)
    {
        _locator = locator ?? new FfmpegLocator();
        _runner = runner ?? new FfmpegProcessRunner(_locator);
    }

    public FfmpegCapabilities GetCapabilities()
    {
        if (_cached != null) return _cached;
        return Task.Run(() => GetCapabilitiesAsync(CancellationToken.None)).GetAwaiter().GetResult();
    }

    public async Task<FfmpegCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        if (_cached != null) return _cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached != null) return _cached;

            var ffmpegPath = _locator.LocateFfmpeg();
            var ffprobePath = _locator.LocateFfprobe();

            if (string.IsNullOrEmpty(ffmpegPath) || string.IsNullOrEmpty(ffprobePath))
            {
                QuickConvertLogger.Info("FfmpegProbeService: FFmpeg or FFprobe binary not found. Capabilities set to unavailable.");
                _cached = FfmpegCapabilities.Unavailable;
                return _cached;
            }

            var version = await ProbeVersionAsync(ffmpegPath, ct).ConfigureAwait(false);
            var encoders = await ProbeEncodersAsync(ffmpegPath, ct).ConfigureAwait(false);

            var validatedHwEncoders = new List<string>();
            foreach (var candidate in CandidateHardwareEncoders)
            {
                if (encoders.Contains(candidate))
                {
                    bool works = await TestEncoderAsync(ffmpegPath, candidate, ct).ConfigureAwait(false);
                    if (works)
                    {
                        validatedHwEncoders.Add(candidate);
                        QuickConvertLogger.Info($"FfmpegProbeService: hardware encoder verified: {candidate}");
                    }
                    else
                    {
                        QuickConvertLogger.Warn($"FfmpegProbeService: hardware encoder {candidate} was listed but failed trial encode; falling back.");
                    }
                }
            }

            _cached = new FfmpegCapabilities(
                isAvailable: true,
                version: version,
                ffmpegPath: ffmpegPath,
                ffprobePath: ffprobePath,
                allEncoders: encoders,
                hardwareEncoders: validatedHwEncoders);

            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> ProbeVersionAsync(string ffmpegPath, CancellationToken ct)
    {
        string? version = null;
        try
        {
            await _runner.RunAsync(
                ffmpegPath,
                ["-version"],
                Environment.CurrentDirectory,
                onStdoutLine: line =>
                {
                    if (version == null && line.StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            version = parts[2];
                        }
                    }
                },
                onStderrLine: null,
                ct: ct,
                idleTimeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Failed to probe FFmpeg version: {ex.Message}");
        }
        return version;
    }

    private async Task<HashSet<string>> ProbeEncodersAsync(string ffmpegPath, CancellationToken ct)
    {
        var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool pastHeader = false;
        try
        {
            await _runner.RunAsync(
                ffmpegPath,
                ["-encoders"],
                Environment.CurrentDirectory,
                onStdoutLine: line =>
                {
                    if (line.StartsWith(" ------", StringComparison.Ordinal) ||
                        line.StartsWith("======", StringComparison.Ordinal) ||
                        line.StartsWith(" Encoders:", StringComparison.Ordinal))
                    {
                        if (line.StartsWith(" ------", StringComparison.Ordinal) || line.StartsWith("======", StringComparison.Ordinal))
                        {
                            pastHeader = true;
                        }
                        return;
                    }
                    if (!pastHeader) return;

                    var match = Regex.Match(line, @"^\s*[V|A|S|D|T][A-Z.]{5}\s+([a-zA-Z0-9_\-]+)");
                    if (match.Success)
                    {
                        encoders.Add(match.Groups[1].Value);
                    }
                },
                onStderrLine: null,
                ct: ct,
                idleTimeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Failed to probe FFmpeg encoders: {ex.Message}");
        }
        return encoders;
    }

    private async Task<bool> TestEncoderAsync(string ffmpegPath, string encoder, CancellationToken ct)
    {
        try
        {
            // 1-frame test transcode: -f lavfi -i color=c=black:s=64x64:d=0.04 -c:v <encoder> -f null -
            var args = new[]
            {
                "-f", "lavfi",
                "-i", "color=c=black:s=64x64:d=0.04",
                "-c:v", encoder,
                "-f", "null",
                "-"
            };

            var exitCode = await _runner.RunAsync(
                ffmpegPath,
                args,
                Environment.CurrentDirectory,
                onStdoutLine: null,
                onStderrLine: null,
                ct: ct,
                idleTimeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            return exitCode == 0;
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Debug($"Encoder trial probe failed for {encoder}: {ex.Message}");
            return false;
        }
    }
}
