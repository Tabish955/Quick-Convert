namespace QuickConvert.Engines.Images;

/// <summary>
/// Moves a finished temporary file to its final name without ever clobbering another file
/// unless the caller asked for overwriting.
/// </summary>
public static class OutputPromotion
{
    private const int MaxAttempts = 9999;

    /// <summary>Promotes <paramref name="tempPath"/> and returns the path it ended up at.</summary>
    /// <param name="tempPath">The fully written temporary file.</param>
    /// <param name="outputDirectory">Folder used when a numbered name is needed.</param>
    /// <param name="baseName">File name without extension, e.g. "photo".</param>
    /// <param name="extension">Extension including the dot, e.g. ".webp".</param>
    /// <param name="firstChoice">The name picked earlier (normally "photo.webp" or "photo (1).webp").</param>
    /// <param name="overwrite">When true, an existing file at <paramref name="firstChoice"/> is replaced.</param>
    /// <exception cref="IOException">No free name was found, or the move failed for a reason other than a name clash.</exception>
    public static string Promote(
        string tempPath,
        string outputDirectory,
        string baseName,
        string extension,
        string firstChoice,
        bool overwrite)
    {
        if (overwrite)
        {
            File.Move(tempPath, firstChoice, overwrite: true);
            return firstChoice;
        }

        var candidate = firstChoice;

        for (int i = 1; i <= MaxAttempts; i++)
        {
            try
            {
                // overwrite: false makes the check and the move one atomic step, so two jobs racing
                // for the same name cannot both win.
                File.Move(tempPath, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                candidate = Path.Combine(outputDirectory, $"{baseName} ({i}){extension}");
            }
        }

        throw new IOException($"Could not find a free file name for '{baseName}{extension}'.");
    }
}
