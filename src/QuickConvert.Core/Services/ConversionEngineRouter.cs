using QuickConvert.Core.Interfaces;

namespace QuickConvert.Core.Services;

/// <summary>
/// Routes (source, target) to the first registered engine that claims it, after the
/// ConversionMatrix has vetted the pair. The matrix always wins: an engine cannot
/// offer a conversion the product scope forbids.
/// </summary>
public sealed class ConversionEngineRouter : IConversionEngineRouter
{
    private readonly IReadOnlyList<IConversionEngine> _engines;

    public ConversionEngineRouter(IEnumerable<IConversionEngine> engines) =>
        _engines = engines.ToList();

    public IConversionEngine? Resolve(string sourceExt, string targetExt)
    {
        var source = FormatRegistry.NormalizeExtension(sourceExt);
        var target = FormatRegistry.NormalizeExtension(targetExt);

        // Matrix first: forbidden pairs are refused before any engine is asked.
        if (!ConversionMatrix.IsAllowed(source, target)) return null;

        return _engines.FirstOrDefault(e => e.CanConvert(source, target));
    }

    public IReadOnlyList<string> GetAvailableTargets(string sourceExt)
    {
        var source = FormatRegistry.NormalizeExtension(sourceExt);
        return ConversionMatrix.GetTargets(source)
            .Where(t => _engines.Any(e => e.CanConvert(source, t)))
            .ToList();
    }

    public string EngineNameFor(string sourceExt, string targetExt) =>
        Resolve(sourceExt, targetExt)?.EngineName ?? string.Empty;
}
