using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;

namespace QuickConvert.Core.Tests;

/// <summary>
/// Deterministic stand-in for a real engine: reports a couple of progress values and succeeds
/// immediately, so coordinator tests do not wait on <see cref="Services.MockConversionEngine"/>'s
/// simulated 2.5 second conversion.
/// </summary>
internal sealed class FakeConversionEngine : IConversionEngine
{
    public string EngineName => "Fake Engine (tests)";

    public IReadOnlyList<string> SupportedSourceExtensions { get; } = [".png", ".jpg", ".txt", ".zip"];

    /// <summary>When true, ConvertAsync returns a failure result instead of a success.</summary>
    public bool FailConversions { get; set; }

    /// <summary>When true, ConvertAsync blocks until the token is cancelled.</summary>
    public bool BlockUntilCancelled { get; set; }

    public bool CanConvert(string sourceExt, string targetExt) => true;

    public async Task<ConversionResult> ConvertAsync(
        ConversionJob job,
        ConversionOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (BlockUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }

        progress?.Report(50);
        job.ProgressPercentage = 50;
        progress?.Report(100);

        var output = Path.ChangeExtension(job.InputFilePath, options.TargetExtension);

        return FailConversions
            ? new ConversionResult(false, string.Empty, "fake failure")
            : new ConversionResult(true, output);
    }
}
