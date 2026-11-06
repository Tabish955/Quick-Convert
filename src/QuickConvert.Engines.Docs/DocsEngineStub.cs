using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;

namespace QuickConvert.Engines.Docs;

/// <summary>
/// Placeholder engine stub for Document, Spreadsheet, and Archive conversions.
/// TODO: Phase 4 - Implement Pandoc, LibreOffice/WPF-Print, and 7-Zip CLI orchestrators.
/// </summary>
public class DocsEngineStub : IConversionEngine
{
    public string EngineName => "DocsEngine (Pandoc / 7-Zip - Phase 4 Stub)";

    public IReadOnlyList<string> SupportedSourceExtensions =>
    [
        .. FormatRegistry.DocumentFormats,
        .. FormatRegistry.SpreadsheetFormats,
        .. FormatRegistry.ArchiveFormats,
        .. FormatRegistry.EBookFormats
    ];

    public bool CanConvert(string sourceExt, string targetExt)
    {
        var srcCat = FormatRegistry.GetCategory(sourceExt);
        return srcCat is FileCategory.Document or FileCategory.Spreadsheet or FileCategory.Archive or FileCategory.EBook;
    }

    public Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        ConversionOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        // TODO: Phase 4 - Wire Pandoc (/tools/pandoc/) and 7-Zip (/tools/7z/)
        throw new NotImplementedException("DocsEngine is slated for implementation in Phase 4.");
    }
}
