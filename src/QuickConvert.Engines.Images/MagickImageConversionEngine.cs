using System.Diagnostics;
using ImageMagick;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;

namespace QuickConvert.Engines.Images;

/// <summary>
/// Image conversion via ImageMagick (Magick.NET-Q8).
/// Read: jpg/jpeg/png/webp/bmp/tiff/gif (first frame)/ico.
/// Write: the same set + pdf (requires the Ghostscript delegate on the machine).
/// HEIC/AVIF are deliberately excluded until the delegate presence is confirmed (decision gate D3).
/// </summary>
public class MagickImageConversionEngine : IConversionEngine
{
    private static readonly string[] Sources = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tiff", ".gif", ".ico"];
    private static readonly string[] Targets = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tiff", ".gif", ".ico", ".pdf"];

    public string EngineName => "ImageMagick";

    public IReadOnlyList<string> SupportedSourceExtensions => Sources;

    public bool CanConvert(string sourceExt, string targetExt) =>
        Sources.Contains(FormatRegistry.NormalizeExtension(sourceExt), StringComparer.OrdinalIgnoreCase)
        && Targets.Contains(FormatRegistry.NormalizeExtension(targetExt), StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(
        ConversionJob job, ConversionOptions options, IProgress<double>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string? outputPath = null;

        try
        {
            ct.ThrowIfCancellationRequested();
            var data = await ReadAllBytesWithProgressAsync(job.InputFilePath, progress, ct);

            // MagickImage reads the first frame only, which is what we want for GIF.
            using var image = new MagickImage(data);
            image.AutoOrient();          // apply EXIF orientation so phone photos aren't sideways
            progress?.Report(10);
            ct.ThrowIfCancellationRequested();

            var targetExt = FormatRegistry.NormalizeExtension(options.TargetExtension);
            if (targetExt is ".jpg" or ".jpeg" or ".webp")
            {
                image.Quality = (uint)Math.Clamp(options.Quality, 1, 100);
            }

            var outDir = !string.IsNullOrWhiteSpace(options.OutputDirectory)
                ? options.OutputDirectory
                : Path.GetDirectoryName(job.InputFilePath) ?? Environment.CurrentDirectory;
            Directory.CreateDirectory(outDir);

            outputPath = GetUniqueOutputPath(
                outDir,
                Path.GetFileNameWithoutExtension(job.InputFilePath),
                targetExt,
                options.OverwriteExisting);

            // image.Write is synchronous with no cancellation hook; run it off the caller's
            // thread and rely on the post-write cancellation check + partial-file cleanup.
            await Task.Run(() => image.Write(outputPath), CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            progress?.Report(85);

            sw.Stop();
            progress?.Report(100);
            return new ConversionResult(true, outputPath, null, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(outputPath);
            return new ConversionResult(false, string.Empty, "Conversion was cancelled by user.", sw.Elapsed);
        }
        catch (Exception ex)   // MagickException, IOException, anything — never throw out of ConvertAsync
        {
            DeleteIfExists(outputPath);
            return new ConversionResult(false, string.Empty, ex.Message, sw.Elapsed);
        }
    }

    /// <summary>photo.png → photo (1).png → photo (2).png. Never overwrites unless told to.</summary>
    private static string GetUniqueOutputPath(string dir, string baseName, string ext, bool overwrite)
    {
        var candidate = Path.Combine(dir, baseName + ext);
        if (overwrite || !File.Exists(candidate)) return candidate;

        for (int i = 1; ; i++)
        {
            candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Decode band is 0–10, scaled by bytes read, so large files show real movement.</summary>
    private static async Task<byte[]> ReadAllBytesWithProgressAsync(
        string path, IProgress<double>? progress, CancellationToken ct)
    {
        var fileInfo = new FileInfo(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        using var ms = new MemoryStream((int)Math.Min(fileInfo.Length, int.MaxValue));
        var buffer = new byte[64 * 1024];
        long totalRead = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await ms.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;
            progress?.Report(10.0 * totalRead / Math.Max(1, fileInfo.Length));
        }
        return ms.ToArray();
    }

    private static void DeleteIfExists(string? path)
    {
        if (path == null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort; a leftover partial file is worse than a swallowed IO error here */ }
    }
}
