using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using QuickConvert.Core.IO;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs.Archives;

public enum ArchiveFailure { None, Unsupported, Corrupt, Encrypted, UnsafePath, Structure }

public sealed record ExtractionProgress(int EntriesDone, int EntriesTotal, string CurrentEntry);

public sealed record ExtractionResult(bool Success, string? OutputDirectory, ArchiveFailure Failure, string Message, int EntriesExtracted)
{
    public ToolResult ToToolResult() => Success ? ToolResult.Ok(OutputDirectory!) : ToolResult.Fail(Message);
}

/// <summary>
/// Extracts zip/7z/tar/gz/rar (read-only for rar) with SharpCompress.
/// Order of defence: (1) validate EVERY entry name and detect encryption before writing a single byte (D3),
/// (2) write only under a private temp folder using paths we built ourselves (never WriteToDirectory),
/// (3) promote the finished folder, numbering it if the name is taken (D4); any failure purges the temp folder.
/// </summary>
public sealed class ArchiveExtractor
{
    public ExtractionResult Extract(string archivePath, string outputDirectory, IProgress<ExtractionProgress>? progress, CancellationToken ct)
    {
        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (!ArchiveFormats.IsExtractable(ext))
            return Fail(ArchiveFailure.Unsupported, $"'{ext}' archives cannot be extracted.");
        if (!File.Exists(archivePath))
            return Fail(ArchiveFailure.Corrupt, "The archive no longer exists.");

        Directory.CreateDirectory(outputDirectory);
        var tempRoot = Path.Combine(outputDirectory, ".tmp-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var folderName = FolderNameFor(archivePath);
            var contentDir = Path.Combine(tempRoot, folderName);
            Directory.CreateDirectory(contentDir);

            var length = new FileInfo(archivePath).Length;
            string openPath = archivePath;
            string? singleGzName = null;

            if (ext == ".gz")
            {
                var inner = Path.Combine(tempRoot, "inner.bin");
                using (var src = File.OpenRead(archivePath))
                using (var gz = new System.IO.Compression.GZipStream(src, System.IO.Compression.CompressionMode.Decompress))
                using (var dst = File.Create(inner))
                {
                    var buf = new byte[81920]; int n;
                    while ((n = gz.Read(buf, 0, buf.Length)) > 0) { ct.ThrowIfCancellationRequested(); dst.Write(buf, 0, n); }
                }

                if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                    archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
                    IsTar(inner))
                {
                    openPath = inner;
                }
                else singleGzName = Path.GetFileNameWithoutExtension(archivePath);

                if (singleGzName is not null)
                {
                    var rel = ArchivePathGuard.Validate(singleGzName);
                    if (!rel.IsValid) return Fail(ArchiveFailure.UnsafePath, UnsafeMessage(rel.Message));
                    File.Move(inner, ArchivePathGuard.Combine(contentDir, rel.RelativePath));
                    progress?.Report(new ExtractionProgress(1, 1, singleGzName));
                    return Promote(contentDir, outputDirectory, folderName, 1);
                }
            }

            using var archive = (ext == ".tar" || openPath != archivePath)
                ? (IArchive)TarArchive.Open(openPath)
                : ArchiveFactory.Open(openPath);

            // ---- Pass 1: validate everything before writing anything ----
            var entries = archive.Entries.ToList();
            if (entries.Count == 0 && length > 100 && (ext == ".zip" || ext == ".7z" || ext == ".rar"))
                return Fail(ArchiveFailure.Corrupt, CorruptMessage);

            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (e.IsEncrypted) return Fail(ArchiveFailure.Encrypted, EncryptedMessage);
                var check = ArchivePathGuard.Validate(e.Key);
                if (!check.IsValid) return Fail(ArchiveFailure.UnsafePath, UnsafeMessage(check.Message));
            }

            // ---- Pass 2: extract ----
            var total = entries.Count(e => !e.IsDirectory);
            var done = 0;
            var used = new RelativePathSet();

            void WriteOne(string key, bool isDirectory, long declaredSize, DateTime? modified, Func<Stream> open)
            {
                ct.ThrowIfCancellationRequested();
                var check = ArchivePathGuard.Validate(key); // defence in depth: never trust pass 1 alone
                if (!check.IsValid) throw new UnsafeEntryException(check.Message);

                if (isDirectory)
                {
                    used.ReserveDirectory(check.RelativePath);
                    Directory.CreateDirectory(ArchivePathGuard.Combine(contentDir, check.RelativePath));
                    return;
                }

                var rel = used.ReserveFile(check.RelativePath);
                var full = ArchivePathGuard.Combine(contentDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                long written;
                using (var input = open())
                using (var output = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    written = output.Length;
                }
                if (declaredSize > 0 && written != declaredSize)
                    throw new InvalidDataException($"Entry '{key}' is truncated ({written} of {declaredSize} bytes).");
                if (modified is { } m) { try { File.SetLastWriteTimeUtc(full, m.ToUniversalTime()); } catch { } }

                done++;
                progress?.Report(new ExtractionProgress(done, total, rel));
            }

            if (archive.IsSolid || archive.Type == SharpCompress.Common.ArchiveType.SevenZip)
            {
                using var reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    var e = reader.Entry;
                    WriteOne(e.Key ?? string.Empty, e.IsDirectory, e.Size, e.LastModifiedTime, () => reader.OpenEntryStream());
                }
            }
            else
            {
                foreach (var e in entries)
                    WriteOne(e.Key ?? string.Empty, e.IsDirectory, e.Size, e.LastModifiedTime, () => e.OpenEntryStream());
            }

            ct.ThrowIfCancellationRequested();
            return Promote(contentDir, outputDirectory, folderName, done);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnsafeEntryException ex) { return Fail(ArchiveFailure.UnsafePath, UnsafeMessage(ex.Message)); }
        catch (ArchiveStructureException ex) { return Fail(ArchiveFailure.Structure, ex.Message + " The archive was not extracted."); }
        catch (Exception ex)
        {
            var kind = Classify(ex);
            return Fail(kind, kind == ArchiveFailure.Encrypted ? EncryptedMessage : CorruptMessage);
        }
        finally
        {
            TempCleanup.Purge(tempRoot); // runs after promotion too: only the empty temp root is left by then
        }
    }

    // ---------- helpers ----------

    internal const string CorruptMessage =
        "This archive is damaged or incomplete and could not be extracted. Nothing was written. Try downloading or copying it again.";

    internal const string EncryptedMessage =
        "This archive is password-protected. Quick Convert cannot extract encrypted archives, so nothing was written.";

    private static string UnsafeMessage(string detail) =>
        $"This archive was rejected for safety: {detail}. It could overwrite files outside the destination, so nothing was extracted.";

    /// <summary>Maps whatever SharpCompress / the BCL throws to a plain category. Public for testing.</summary>
    public static ArchiveFailure Classify(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is CryptographicException) return ArchiveFailure.Encrypted;
            var m = e.Message ?? string.Empty;
            if (m.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
                return ArchiveFailure.Encrypted;
        }
        return ArchiveFailure.Corrupt;
    }

    private static ExtractionResult Promote(string contentDir, string outputDirectory, string folderName, int count)
    {
        var final = PromotionHelper.PromoteDirectory(contentDir, Path.Combine(outputDirectory, folderName));
        return new ExtractionResult(true, final, ArchiveFailure.None, string.Empty, count);
    }

    private static ExtractionResult Fail(ArchiveFailure f, string message) => new(false, null, f, message, 0);

    /// <summary>"photos.zip" → "photos", "backup.tar.gz" → "backup". Never empty, never contains invalid chars.</summary>
    public static string FolderNameFor(string archivePath)
    {
        var name = Path.GetFileNameWithoutExtension(archivePath);
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "extracted" : name;
    }

    private static bool IsTar(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (TarArchive.IsTarFile(fs)) return true;
        }
        catch { }

        try
        {
            using var tar = TarArchive.Open(path);
            return tar.Entries.Any();
        }
        catch { }

        return false;
    }

    private sealed class UnsafeEntryException(string m) : Exception(m);
}
