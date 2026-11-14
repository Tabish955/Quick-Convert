using System.Text.RegularExpressions;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using QuickConvert.Core.IO;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs.Archives;

public sealed record CompressionRequest(string SourceDirectory, string TargetExtension, string OutputDirectory, bool Overwrite);

/// <summary>
/// Folder → archive. zip/tar/gz via SharpCompress writers; 7z only via a pinned 7za subprocess
/// (SharpCompress cannot create 7z). ".gz" means tar.gz (D5) because bare gzip cannot hold a folder.
/// RAR is never a target.
/// </summary>
public sealed class ArchiveCompressor
{
    public const string SevenZipMissing = "7-Zip (7za.exe) is required to create .7z archives. Run tools/Get-7za.ps1 to install it.";

    private static readonly Regex Percent = new(@"(\d{1,3})%", RegexOptions.Compiled);

    private readonly IToolProcessRunner _runner;
    private readonly Func<string?> _locate7za;

    public ArchiveCompressor(IToolProcessRunner runner, Func<string?> locate7za)
    {
        _runner = runner; _locate7za = locate7za;
    }

    public bool CanCompressTo(string targetExt)
    {
        if (!ArchiveFormats.IsValidTarget(targetExt)) return false;
        return !targetExt.Equals(".7z", StringComparison.OrdinalIgnoreCase) || _locate7za() is not null;
    }

    public async Task<ToolResult> CompressAsync(CompressionRequest req, IProgress<int>? progress, CancellationToken ct)
    {
        var tgt = req.TargetExtension.ToLowerInvariant();
        if (!ArchiveFormats.IsValidTarget(tgt))
            return ToolResult.Fail(tgt == ".rar"
                ? "RAR archives cannot be created (read-only format)."
                : $"'{tgt}' is not a supported archive format.");
        if (!Directory.Exists(req.SourceDirectory))
            return ToolResult.Fail("The folder no longer exists.");

        string? sevenZip = null;
        if (tgt == ".7z")
        {
            sevenZip = _locate7za();
            if (sevenZip is null) return ToolResult.Fail(SevenZipMissing);
        }

        var sourceFull = Path.GetFullPath(req.SourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var baseName = Path.GetFileName(sourceFull);
        if (string.IsNullOrEmpty(baseName)) baseName = "archive";
        var finalExt = tgt == ".gz" ? ".tar.gz" : tgt;

        Directory.CreateDirectory(req.OutputDirectory);
        var tempDir = Path.Combine(req.OutputDirectory, ".tmp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            progress?.Report(0);
            var tempOut = Path.Combine(tempDir, "out" + finalExt);

            // The output must never land inside the folder being archived (it would archive itself).
            var outFull = Path.GetFullPath(req.OutputDirectory);
            var insideSource = outFull.StartsWith(sourceFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                               || outFull.Equals(sourceFull, StringComparison.OrdinalIgnoreCase);

            if (tgt == ".7z")
            {
                if (insideSource) return ToolResult.Fail("Choose an output folder outside the folder you are archiving.");
                var args = new List<string> { "a", "-t7z", "-y", "-mx=5", "-bsp1", "-bso0", "-snl", tempOut, "*" };
                var pr = await _runner.RunAsync(sevenZip!, args, sourceFull, line =>
                {
                    var m = Percent.Match(line);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var pct)) progress?.Report(Math.Min(94, pct * 95 / 100));
                }, ct).ConfigureAwait(false);

                if (pr.ExitCode != 0 || !File.Exists(tempOut))
                    return ToolResult.Fail("7-Zip could not create the archive: " +
                        (string.IsNullOrWhiteSpace(pr.StdErrTail) ? $"exit code {pr.ExitCode}" : pr.StdErrTail.Trim()));
            }
            else
            {
                await Task.Run(() => WriteWithSharpCompress(sourceFull, tgt, tempOut, progress, ct), ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(95);
            var final = PromotionHelper.PromoteFile(tempOut, Path.Combine(req.OutputDirectory, baseName + finalExt), req.Overwrite);
            progress?.Report(100);
            return ToolResult.Ok(final);
        }
        finally { TempCleanup.Purge(tempDir); }
    }

    private static void WriteWithSharpCompress(string sourceDir, string tgt, string tempOut, IProgress<int>? progress, CancellationToken ct)
    {
        var files = EnumerateFiles(sourceDir).ToList();
        using var fs = File.Create(tempOut);

        using var writer = tgt switch
        {
            ".zip" => WriterFactory.Open(fs, ArchiveType.Zip, new ZipWriterOptions(CompressionType.Deflate) { UseZip64 = true }),
            ".tar" => WriterFactory.Open(fs, ArchiveType.Tar, new WriterOptions(CompressionType.None)),
            ".gz" => WriterFactory.Open(fs, ArchiveType.Tar, new WriterOptions(CompressionType.GZip)),
            _ => throw new NotSupportedException(tgt)
        };

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var full = files[i];
            var rel = Path.GetRelativePath(sourceDir, full).Replace(Path.DirectorySeparatorChar, '/');
            using var input = File.OpenRead(full);
            writer.Write(rel, input, File.GetLastWriteTimeUtc(full));
            progress?.Report((i + 1) * 95 / Math.Max(1, files.Count));
        }
    }

    /// <summary>Recursive walk that never follows junctions, symlinks or other reparse points.</summary>
    public static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                yield return file;
            }
            foreach (var sub in Directory.EnumerateDirectories(dir).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(sub);
            }
        }
    }
}
