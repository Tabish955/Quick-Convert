using System.Diagnostics;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Engines.Docs.Documents;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs;

/// <summary>Documents (Pandoc, HTML path) and PDF output (Pandoc → HTML → wkhtmltopdf). Replaces DocsEngineStub.</summary>
public sealed class PandocConversionEngine : IConversionEngine
{
    private readonly DocumentConverter _converter;

    public PandocConversionEngine()
        : this(new DocumentConverter(new ToolProcessRunner(),
            ToolVersions.PandocLocator().Locate, ToolVersions.WkhtmltopdfLocator().Locate)) { }

    public PandocConversionEngine(DocumentConverter converter) => _converter = converter;

    public string EngineName => "PandocDocumentEngine";

    public IReadOnlyList<string> SupportedSourceExtensions => [.. DocumentFormats.Sources];

    public bool CanConvert(string sourceExt, string targetExt) => _converter.CanConvert(sourceExt, targetExt);

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
            var result = await _converter.ConvertAsync(
                new DocumentRequest(v.SourcePath, v.TargetExtension, v.OutputDirectory, v.Overwrite),
                prog,
                ct);
            return EngineBridge.ToResult(result, sw.Elapsed);
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
