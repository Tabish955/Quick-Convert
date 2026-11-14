using QuickConvert.Core.IO;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs.Documents;

public sealed record DocumentRequest(string SourcePath, string TargetExtension, string OutputDirectory, bool Overwrite);

/// <summary>
/// Pandoc document conversions (HTML path).
/// Everything is written into a private ".tmp_<guid>" folder and only the finished result is promoted.
/// </summary>
public sealed class DocumentConverter
{
    public const string PandocMissing = "Pandoc is required for document conversions. Run tools/Get-Pandoc.ps1 to install it.";

    private readonly IToolProcessRunner _runner;
    private readonly Func<string?> _locatePandoc;

    public DocumentConverter(IToolProcessRunner runner, Func<string?> locatePandoc)
    {
        _runner = runner;
        _locatePandoc = locatePandoc;
    }

    /// <summary>True only if the pair is supported AND Pandoc is installed (targets are hidden otherwise).</summary>
    public bool CanConvert(string sourceExt, string targetExt)
    {
        if (DocumentFormats.IsPandocPair(sourceExt, targetExt)) return _locatePandoc() is not null;
        return false;
    }

    public async Task<ToolResult> ConvertAsync(DocumentRequest req, IProgress<int>? progress, CancellationToken ct)
    {
        var src = Path.GetExtension(req.SourcePath).ToLowerInvariant();
        var tgt = req.TargetExtension.ToLowerInvariant();

        if (!DocumentFormats.IsPandocPair(src, tgt))
            return ToolResult.Fail($"Converting {src} to {tgt} is not supported.");
        if (!File.Exists(req.SourcePath))
            return ToolResult.Fail("The source file no longer exists.");

        var pandoc = _locatePandoc();
        if (pandoc is null) return ToolResult.Fail(PandocMissing);

        var stem = Path.GetFileNameWithoutExtension(req.SourcePath);
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(req.SourcePath))!;
        Directory.CreateDirectory(req.OutputDirectory);
        var tempDir = Path.Combine(req.OutputDirectory, ".tmp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            progress?.Report(0);
            var tempOut = Path.Combine(tempDir, "out" + tgt);
            string? mediaName = null;

            if (tgt == ".md" && (src == ".docx" || src == ".odt" || src == ".rtf"))
            {
                var freeMedia = CollisionNamer.NextAvailableDirectory(
                    Path.Combine(req.OutputDirectory, stem + "_media"),
                    p => Directory.Exists(p) || File.Exists(p));
                mediaName = Path.GetFileName(freeMedia);
            }

            var pr = await _runner.RunAsync(pandoc,
                PandocArgumentBuilder.Build(src, tgt, Path.GetFullPath(req.SourcePath), tempOut, sourceDir, mediaName),
                tempDir, null, ct).ConfigureAwait(false);
            if (pr.ExitCode != 0 || !File.Exists(tempOut))
                return ToolResult.Fail(Describe("Pandoc could not convert this document", pr));

            ct.ThrowIfCancellationRequested();
            progress?.Report(95); // subprocess finished; 100 only after promotion

            // Promote media first, then the document (the document is what the user sees).
            if (mediaName is not null)
            {
                var producedMedia = Path.Combine(tempDir, mediaName);
                if (Directory.Exists(producedMedia))
                    PromotionHelper.PromoteDirectory(producedMedia, Path.Combine(req.OutputDirectory, mediaName));
            }

            var final = PromotionHelper.PromoteFile(tempOut, Path.Combine(req.OutputDirectory, stem + tgt), req.Overwrite);
            progress?.Report(100);
            return ToolResult.Ok(final);
        }
        finally
        {
            TempCleanup.Purge(tempDir);
        }
    }

    private static string Describe(string headline, ProcessResult r)
    {
        var tail = string.IsNullOrWhiteSpace(r.StdErrTail) ? $"exit code {r.ExitCode}" : r.StdErrTail.Trim();
        return $"{headline}: {tail}";
    }
}
