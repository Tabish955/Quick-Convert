namespace QuickConvert.Core.Models;

/// <summary>
/// Encapsulates the outcome of a completed, failed, or cancelled conversion attempt.
/// </summary>
public record ConversionResult(
    bool Success,
    string OutputFilePath,
    string? ErrorMessage = null,
    TimeSpan Duration = default
);
