using System.Diagnostics;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;

namespace QuickConvert.Core.Services;

/// <summary>
/// Mock conversion engine for Phase 1 and Phase 2 testing.
/// Simulates a 2.5-second realistic conversion with 50ms incremental progress updates,
/// cancellation support, and dummy output file generation.
/// </summary>
public class MockConversionEngine : IConversionEngine
{
    public string EngineName => "QuickConvert Mock Engine";

    private static readonly List<string> AllSupportedExtensions =
    [
        .. FormatRegistry.ImageFormats,
        .. FormatRegistry.VideoFormats,
        .. FormatRegistry.AudioFormats,
        .. FormatRegistry.DocumentFormats,
        .. FormatRegistry.SpreadsheetFormats,
        .. FormatRegistry.ArchiveFormats,
        .. FormatRegistry.EBookFormats,
        .. FormatRegistry.FontFormats
    ];

    public IReadOnlyList<string> SupportedSourceExtensions => AllSupportedExtensions;

    public bool CanConvert(string sourceExt, string targetExt)
    {
        // Mock engine supports all registered formats
        return FormatRegistry.IsSupported(sourceExt);
    }

    public async Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        ConversionOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            job.Status = ConversionStatus.Converting;
            job.StatusText = "Converting...";
            job.ProgressPercentage = 0;
            progress?.Report(0);

            // Determine target extension
            var targetExt = !string.IsNullOrWhiteSpace(options.TargetExtension)
                ? FormatRegistry.NormalizeExtension(options.TargetExtension)
                : FormatRegistry.NormalizeExtension(job.TargetExtension);

            if (string.IsNullOrEmpty(targetExt))
            {
                var supported = FormatRegistry.GetSupportedTargetFormats(job.SourceExtension);
                targetExt = supported.Count > 0 ? supported[0] : ".bin";
                job.TargetExtension = targetExt;
            }

            // Determine output directory
            var outDir = !string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? options.OutputDirectory
                : (!string.IsNullOrWhiteSpace(job.InputFilePath) ? Path.GetDirectoryName(job.InputFilePath) : null);

            if (string.IsNullOrWhiteSpace(outDir))
            {
                outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            Directory.CreateDirectory(outDir);

            // Determine unique target file path
            var baseName = Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(job.InputFilePath) ? "ConvertedFile" : job.InputFilePath);
            var destinationFilePath = Path.Combine(outDir, $"{baseName}{targetExt}");

            if (!options.OverwriteExisting && File.Exists(destinationFilePath))
            {
                var counter = 1;
                while (File.Exists(destinationFilePath))
                {
                    destinationFilePath = Path.Combine(outDir, $"{baseName}_{counter}{targetExt}");
                    counter++;
                }
            }

            // Simulate realistic 2.5 second conversion (50 steps x 50ms)
            const int totalSteps = 50;
            const int stepDelayMs = 50;

            for (int step = 1; step <= totalSteps; step++)
            {
                ct.ThrowIfCancellationRequested();

                await Task.Delay(stepDelayMs, ct);

                double currentProgress = (double)step / totalSteps * 100.0;
                job.ProgressPercentage = Math.Round(currentProgress, 1);
                job.StatusText = $"Converting ({job.ProgressPercentage:0}%)";
                progress?.Report(job.ProgressPercentage);
            }

            // Write realistic dummy content
            var dummyBanner = $"""
                =============================================================
                QuickConvert (Phase 1 Mock Engine Output)
                =============================================================
                Source File:     {job.InputFilePath}
                Source Size:     {job.FormattedFileSize} ({job.FileSizeBytes} bytes)
                Target Format:   {targetExt}
                Quality Setting: {options.Quality}
                Converted At:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                Duration:        {sw.Elapsed.TotalSeconds:F2} seconds
                Status:          Successfully Converted (Mock)
                =============================================================
                """;

            await File.WriteAllTextAsync(destinationFilePath, dummyBanner, CancellationToken.None);

            sw.Stop();
            job.Status = ConversionStatus.Completed;
            job.StatusText = "Completed";
            job.ProgressPercentage = 100;
            job.OutputFilePath = destinationFilePath;
            progress?.Report(100);

            return new ConversionResult(
                Success: true,
                OutputFilePath: destinationFilePath,
                ErrorMessage: null,
                Duration: sw.Elapsed
            );
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            job.Status = ConversionStatus.Cancelled;
            job.StatusText = "Cancelled";
            return new ConversionResult(
                Success: false,
                OutputFilePath: string.Empty,
                ErrorMessage: "Conversion was cancelled by user.",
                Duration: sw.Elapsed
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            job.Status = ConversionStatus.Failed;
            job.StatusText = "Failed";
            job.ErrorMessage = ex.Message;
            return new ConversionResult(
                Success: false,
                OutputFilePath: string.Empty,
                ErrorMessage: ex.Message,
                Duration: sw.Elapsed
            );
        }
    }
}
