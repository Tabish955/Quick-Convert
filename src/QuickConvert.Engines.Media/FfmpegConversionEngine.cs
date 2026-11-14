using System.Diagnostics;
using System.Globalization;
using QuickConvert.Core.Diagnostics;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.IO;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;

namespace QuickConvert.Engines.Media;

/// <summary>
/// Media conversion engine using FFmpeg and FFprobe as isolated child processes.
/// Converts between audio and video formats, probes stream durations, reports real-time progress,
/// supports hardware encoding with automatic software fallback, and ensures safe atomic output promotion.
/// </summary>
public class FfmpegConversionEngine : IConversionEngine, IConcurrencyAwareEngine
{
    public int RecommendedMaxDegreeOfParallelism => Math.Max(1, Environment.ProcessorCount / 4);
    private static readonly string[] Sources =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv",
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma"
    ];

    private readonly IFfmpegProcessRunner _runner;
    private readonly FfmpegLocator _locator;
    private readonly IFfmpegProbeService _probeService;

    public FfmpegConversionEngine(
        IFfmpegProcessRunner? runner = null,
        FfmpegLocator? locator = null,
        IFfmpegProbeService? probeService = null)
    {
        _locator = locator ?? new FfmpegLocator();
        _runner = runner ?? new FfmpegProcessRunner(_locator);
        _probeService = probeService ?? new FfmpegProbeService(_runner, _locator);
    }

    public string EngineName => "FFmpeg";

    public IReadOnlyList<string> SupportedSourceExtensions => Sources;

    public bool CanConvert(string sourceExt, string targetExt)
    {
        if (!_locator.IsFfmpegAvailable)
        {
            return false;
        }

        var normSource = FormatRegistry.NormalizeExtension(sourceExt);
        var normTarget = FormatRegistry.NormalizeExtension(targetExt);

        if (!Sources.Contains(normSource, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return ConversionMatrix.IsAllowed(normSource, normTarget);
    }

    public async Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        ConversionOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string? tempPath = null;
        string? outputPath = null;

        try
        {
            ct.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(job.InputFilePath);
            if (!fileInfo.Exists)
            {
                return new ConversionResult(false, string.Empty, $"File not found: {job.InputFilePath}", sw.Elapsed);
            }

            var ffmpegPath = _locator.LocateFfmpeg();
            var ffprobePath = _locator.LocateFfprobe();

            if (string.IsNullOrWhiteSpace(ffmpegPath) || string.IsNullOrWhiteSpace(ffprobePath))
            {
                return new ConversionResult(
                    false,
                    string.Empty,
                    "FFmpeg is required for audio and video conversions. Run tools/Get-Ffmpeg.ps1 to install it.",
                    sw.Elapsed);
            }

            var targetExt = FormatRegistry.NormalizeExtension(
                !string.IsNullOrWhiteSpace(options.TargetExtension) ? options.TargetExtension : job.TargetExtension);

            var outDir = !string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? options.OutputDirectory
                : Path.GetDirectoryName(job.InputFilePath) ?? Environment.CurrentDirectory;

            Directory.CreateDirectory(outDir);

            var baseName = Path.GetFileNameWithoutExtension(job.InputFilePath);
            outputPath = OutputPromotion.GetUniqueOutputPath(outDir, baseName, targetExt, options.OverwriteExisting);

            // Transcode to a unique temporary file first
            tempPath = Path.Combine(outDir, $".tmp_{Guid.NewGuid():N}{targetExt}");

            // Probe stream duration for accurate percentage progress
            double durationSeconds = await ProbeDurationSecondsAsync(ffprobePath, job.InputFilePath, ct).ConfigureAwait(false);
            var progressParser = new FfmpegProgressParser(durationSeconds, progress);

            // Query probed hardware capabilities
            var caps = await _probeService.GetCapabilitiesAsync(ct).ConfigureAwait(false);
            string? preferredVideoEnc = caps.PreferredH264Encoder;

            var args = FfmpegArgumentBuilder.BuildArguments(
                job.InputFilePath,
                tempPath,
                fileInfo.Extension,
                targetExt,
                preferredVideoEnc);

            int exitCode = await _runner.RunAsync(
                ffmpegPath,
                args,
                outDir,
                onStdoutLine: line => progressParser.ProcessLine(line),
                onStderrLine: null,
                ct: ct,
                idleTimeout: TimeSpan.FromSeconds(60)).ConfigureAwait(false);

            // If transcode failed with a hardware encoder, attempt software fallback (libx264)
            if (exitCode != 0 && preferredVideoEnc != null && !preferredVideoEnc.Equals("libx264", StringComparison.OrdinalIgnoreCase))
            {
                QuickConvertLogger.Warn($"FFmpeg transcode failed with hardware encoder {preferredVideoEnc} (exit code {exitCode}); retrying with libx264 software encoder.");
                DeleteIfExists(tempPath);
                tempPath = Path.Combine(outDir, $".tmp_{Guid.NewGuid():N}{targetExt}");

                var fallbackArgs = FfmpegArgumentBuilder.BuildArguments(
                    job.InputFilePath,
                    tempPath,
                    fileInfo.Extension,
                    targetExt,
                    "libx264");

                exitCode = await _runner.RunAsync(
                    ffmpegPath,
                    fallbackArgs,
                    outDir,
                    onStdoutLine: line => progressParser.ProcessLine(line),
                    onStderrLine: null,
                    ct: ct,
                    idleTimeout: TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            }

            if (exitCode != 0)
            {
                var stderr = _runner.GetRecentStderrText();
                DeleteIfExists(tempPath);
                var message = string.IsNullOrWhiteSpace(stderr)
                    ? $"FFmpeg process failed with exit code {exitCode}."
                    : $"FFmpeg error: {stderr}";
                return new ConversionResult(false, string.Empty, message, sw.Elapsed);
            }

            ct.ThrowIfCancellationRequested();

            // Promote temporary transcode file to destination
            outputPath = OutputPromotion.Promote(
                tempPath,
                outDir,
                baseName,
                targetExt,
                outputPath,
                options.OverwriteExisting);

            tempPath = null; // Disarmed: destination is safely promoted
            progress?.Report(100.0);
            sw.Stop();

            return new ConversionResult(true, outputPath, null, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(tempPath);
            return new ConversionResult(false, string.Empty, "Conversion was cancelled by user.", sw.Elapsed);
        }
        catch (Exception ex)
        {
            DeleteIfExists(tempPath);
            return new ConversionResult(false, string.Empty, ex.Message, sw.Elapsed);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    private async Task<double> ProbeDurationSecondsAsync(string ffprobePath, string inputPath, CancellationToken ct)
    {
        double duration = 0.0;
        try
        {
            var args = new[]
            {
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                inputPath
            };

            await _runner.RunAsync(
                ffprobePath,
                args,
                Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory,
                onStdoutLine: line =>
                {
                    if (double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0)
                    {
                        duration = parsed;
                    }
                },
                onStderrLine: null,
                ct: ct,
                idleTimeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Failed to probe media duration: {ex.Message}");
        }
        return duration;
    }

    private static void DeleteIfExists(string? path)
    {
        if (path == null) return;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }
}

