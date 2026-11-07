using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;

namespace QuickConvert.Core.Tests;

/// <summary>
/// Second fake engine, for router tests: claims only .txt/.md ↔ .txt/.md.
/// Stands in for the future Pandoc engine.
/// </summary>
public class FakeDocsEngine : IConversionEngine
{
    public string EngineName => "Fake Docs";

    public IReadOnlyList<string> SupportedSourceExtensions => [".txt", ".md"];

    public bool CanConvert(string sourceExt, string targetExt)
    {
        var s = Services.FormatRegistry.NormalizeExtension(sourceExt);
        var t = Services.FormatRegistry.NormalizeExtension(targetExt);
        return s is ".txt" or ".md" && t is ".txt" or ".md";
    }

    public Task<ConversionResult> ConvertAsync(
        ConversionJob job, ConversionOptions options, IProgress<double>? progress, CancellationToken ct) =>
        Task.FromResult(new ConversionResult(true, job.InputFilePath + ".out"));
}
