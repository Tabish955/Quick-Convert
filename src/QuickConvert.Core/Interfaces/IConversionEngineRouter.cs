namespace QuickConvert.Core.Interfaces;

/// <summary>
/// Resolves (source, target) pairs to the engine that can perform them, and is the
/// only authority for which targets the UI may offer: matrix ∩ engine capability.
/// </summary>
public interface IConversionEngineRouter
{
    /// <summary>Returns the engine that can convert source→target, or null if none can.</summary>
    IConversionEngine? Resolve(string sourceExt, string targetExt);

    /// <summary>Target extensions the matrix allows for source AND at least one engine can produce.</summary>
    IReadOnlyList<string> GetAvailableTargets(string sourceExt);

    /// <summary>Descriptive engine name for a conversion, or string.Empty if unsupported.</summary>
    string EngineNameFor(string sourceExt, string targetExt);
}
