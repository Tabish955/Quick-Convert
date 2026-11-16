namespace QuickConvert.Core.Tools;

/// <summary>
/// Result of probing installed external conversion tools.
/// </summary>
public sealed record ToolProbeResult(
    bool AllPresent,
    IReadOnlyList<MissingToolInfo> MissingTools
);
