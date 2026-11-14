using System.Diagnostics;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Engines.Docs.Spreadsheets;

namespace QuickConvert.Engines.Docs;

public sealed class SpreadsheetConversionEngine : IConversionEngine
{
    private readonly SpreadsheetConverter _converter;

    public SpreadsheetConversionEngine() : this(new SpreadsheetConverter()) { }

    public SpreadsheetConversionEngine(SpreadsheetConverter converter) => _converter = converter;

    public string EngineName => "SpreadsheetEngine";

    public IReadOnlyList<string> SupportedSourceExtensions => [.. SpreadsheetConverter.Sources];

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
                new SpreadsheetRequest(v.SourcePath, v.TargetExtension, v.OutputDirectory, v.Overwrite),
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
