using System.Diagnostics;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Engines.Docs.Archives;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs;

/// <summary>
/// Extract: archive file + "#extract" → folder (job.TargetKind == Directory, set by the coordinator).
/// Compress: folder + .zip/.7z/.tar/.gz → archive file (job.SourceKind == Directory).
/// RAR is only ever a source.
/// </summary>
public sealed class ArchiveConversionEngine : IConversionEngine
{
    private readonly ArchiveExtractor _extractor = new();
    private readonly ArchiveCompressor _compressor;

    public ArchiveConversionEngine() : this(new ArchiveCompressor(new ToolProcessRunner(), ToolVersions.SevenZipLocator().Locate)) { }
    public ArchiveConversionEngine(ArchiveCompressor compressor) => _compressor = compressor;

    public string EngineName => "ArchiveEngine";

    public IReadOnlyList<string> SupportedSourceExtensions => [.. ArchiveFormats.ExtractableSources];

    /// <summary>For extraction the "source extension" is the archive's; for compression the folder has no extension, so pass "" or "<dir>".</summary>
    public bool CanConvert(string sourceExt, string targetExt)
    {
        if (targetExt == ConversionTargets.Extract) return ArchiveFormats.IsExtractable(sourceExt);
        if (string.IsNullOrEmpty(sourceExt) || sourceExt == "<dir>") return _compressor.CanCompressTo(targetExt);
        return false;
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
            var v = EngineBridge.Read(job, options);
            var prog = EngineBridge.Progress(progress);

            if (v.TargetExtension == ConversionTargets.Extract)
            {
                var report = prog is null ? null
                    : new Progress<ExtractionProgress>(p => prog.Report(p.EntriesTotal == 0 ? 0 : Math.Min(95, p.EntriesDone * 95 / p.EntriesTotal)));
                var r = await Task.Run(() => _extractor.Extract(v.SourcePath, v.OutputDirectory, report, ct), ct);
                if (r.Success) prog?.Report(100); // only after promotion
                return EngineBridge.ToResult(r.ToToolResult(), sw.Elapsed);
            }

            var compResult = await _compressor.CompressAsync(
                new CompressionRequest(v.SourcePath, v.TargetExtension, v.OutputDirectory, v.Overwrite),
                prog,
                ct);
            return EngineBridge.ToResult(compResult, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return new ConversionResult(false, string.Empty, "Conversion was cancelled by user.", sw.Elapsed);
        }
        catch (Exception ex)
        {
            return new ConversionResult(false, string.Empty, ex.Message, sw.Elapsed);
        }
    }
}
