namespace QuickConvert.Engines.Images;

/// <summary>
/// Type forwarder to <see cref="QuickConvert.Core.IO.OutputPromotion"/> to maintain
/// backward compatibility for existing callers and test suites.
/// </summary>
public static class OutputPromotion
{
    /// <summary>Promotes <paramref name="tempPath"/> and returns the path it ended up at.</summary>
    public static string Promote(
        string tempPath,
        string outputDirectory,
        string baseName,
        string extension,
        string firstChoice,
        bool overwrite) =>
        QuickConvert.Core.IO.OutputPromotion.Promote(tempPath, outputDirectory, baseName, extension, firstChoice, overwrite);
}
