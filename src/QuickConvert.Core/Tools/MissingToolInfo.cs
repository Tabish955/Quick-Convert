namespace QuickConvert.Core.Tools;

/// <summary>
/// Describes a missing external CLI tool, its installer script, and affected features.
/// </summary>
public sealed record MissingToolInfo(
    string ToolName,
    string ExeName,
    string SetupScript,
    string AffectedConversions
);
