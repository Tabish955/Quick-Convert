using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;

namespace QuickConvert.Core.Services;

/// <summary>
/// Routes (source, target) to the first registered engine that claims it, after the
/// ConversionMatrix has vetted the pair. The matrix always wins: an engine cannot
/// offer a conversion the product scope forbids.
/// Reserved targets (such as "#extract") and folder sources bypass the matrix and
/// query registered engines directly.
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

        if (ConversionTargets.IsReserved(target) || string.IsNullOrEmpty(source) || source == "<dir>")
        {
            return _engines.FirstOrDefault(e => e.CanConvert(source, target));
        }

        // Matrix first: forbidden pairs are refused before any engine is asked.
        if (!ConversionMatrix.IsAllowed(source, target)) return null;

        return _engines.FirstOrDefault(e => e.CanConvert(source, target));
    }

    public IReadOnlyList<string> GetAvailableTargets(string sourceExt)
    {
        var source = FormatRegistry.NormalizeExtension(sourceExt);

        if (string.IsNullOrEmpty(source) || source == "<dir>")
        {
            string[] archiveTargets = [".zip", ".7z", ".tar", ".gz"];
            return archiveTargets.Where(t => _engines.Any(e => e.CanConvert(source, t))).ToList();
        }

        var targets = ConversionMatrix.GetTargets(source)
            .Where(t => _engines.Any(e => e.CanConvert(source, t)))
            .ToList();

        if (_engines.Any(e => e.CanConvert(source, ConversionTargets.Extract)))
        {
            targets.Insert(0, ConversionTargets.Extract);
        }

        return targets;
    }

    public string EngineNameFor(string sourceExt, string targetExt) =>
        Resolve(sourceExt, targetExt)?.EngineName ?? string.Empty;
}
