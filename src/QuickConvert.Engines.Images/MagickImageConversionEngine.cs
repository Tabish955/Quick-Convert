using System.Diagnostics;
using ImageMagick;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;

namespace QuickConvert.Engines.Images;

/// <summary>
/// Image conversion via ImageMagick (Magick.NET-Q8).
/// Read: jpg/jpeg/png/webp/bmp/tiff/gif (first frame)/ico.
/// Write: the same set + pdf (writing PDF is supported natively; Ghostscript is only needed if rasterising/reading PDF).
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
        string? tempPath = null;

        try
        {
            ct.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(job.InputFilePath);
            if (!fileInfo.Exists)
            {
                return new ConversionResult(false, string.Empty, $"File not found: {job.InputFilePath}", sw.Elapsed);
            }

            // Stream directly from the file without reading the whole file into a byte[] upfront.
            // ProgressStream wraps FileStream and reports decode progress in the 0..10 band.
            await using var stream = new FileStream(job.InputFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: false);
            using var progressStream = new ProgressStream(stream, fileInfo.Length, progress, ct);

            // MagickImage reads straight from the stream (first frame only for GIF/multipage).
            using var image = new MagickImage(progressStream);
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

            // Write to a temporary file in the same folder first so existing destination files
            // are not destroyed if conversion is cancelled or encounters an error.
            tempPath = Path.Combine(outDir, $".tmp_{Guid.NewGuid():N}{targetExt}");

            // image.Write is synchronous with no cancellation hook; run it off the caller's
            // thread and rely on the post-write cancellation check + partial-file cleanup.
            await Task.Run(() => image.Write(tempPath), CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            progress?.Report(85);

            // Promote the temporary file to its final name. Unless overwriting was requested this never
            // clobbers a file that appeared after the name was chosen (e.g. a.jpg and a.png in one batch
            // both converting to a.webp): it takes the next free "name (n)" instead.
            outputPath = OutputPromotion.Promote(
                tempPath,
                outDir,
                Path.GetFileNameWithoutExtension(job.InputFilePath),
                targetExt,
                outputPath,
                options.OverwriteExisting);
            tempPath = null; // disarmed: final file should not be removed

            sw.Stop();
            progress?.Report(100);
            return new ConversionResult(true, outputPath, null, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(tempPath);
            return new ConversionResult(false, string.Empty, "Conversion was cancelled by user.", sw.Elapsed);
        }
        catch (Exception ex)   // MagickException, IOException, anything — never throw out of ConvertAsync
        {
            DeleteIfExists(tempPath);
            return new ConversionResult(false, string.Empty, ex.Message, sw.Elapsed);
        }
        finally
        {
            DeleteIfExists(tempPath);
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

    private static void DeleteIfExists(string? path)
    {
        if (path == null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort; a leftover partial file is worse than a swallowed IO error here */ }
    }

    /// <summary>
    /// Stream wrapper that passes reads to the underlying stream while reporting read progress in the 0..10 band.
    /// Supports seeking so format decoders that inspect headers can navigate without error.
    /// </summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _totalLength;
        private readonly IProgress<double>? _progress;
        private readonly CancellationToken _ct;

        public ProgressStream(Stream inner, long totalLength, IProgress<double>? progress, CancellationToken ct)
        {
            _inner = inner;
            _totalLength = Math.Max(1, totalLength);
            _progress = progress;
            _ct = ct;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _ct.ThrowIfCancellationRequested();
            var read = _inner.Read(buffer, offset, count);
            ReportProgress();
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            _ct.ThrowIfCancellationRequested();
            var read = _inner.Read(buffer);
            ReportProgress();
            return read;
        }

        private void ReportProgress()
        {
            if (_inner.CanSeek)
            {
                var pos = _inner.Position;
                _progress?.Report(10.0 * Math.Clamp(pos, 0, _totalLength) / _totalLength);
            }
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
