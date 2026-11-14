namespace QuickConvert.Engines.Docs.Documents;

public static class DocumentFormats
{
    /// <summary>Roadmap document formats readable by Pandoc. PDF is a target only (Pandoc cannot read it).</summary>
    public static readonly IReadOnlySet<string> Sources =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".html", ".docx", ".rtf", ".odt" };

    public static readonly IReadOnlySet<string> PandocTargets =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".html", ".docx", ".rtf", ".odt" };

    public const string Pdf = ".pdf";

    public static bool IsPandocPair(string src, string tgt) =>
        Sources.Contains(src) && PandocTargets.Contains(tgt) && !string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase);

    public static bool IsPdfPair(string src, string tgt) =>
        Sources.Contains(src) && string.Equals(tgt, Pdf, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Pure argument construction. Tested without Pandoc installed.</summary>
public static class PandocArgumentBuilder
{
    public static string Reader(string ext) => ext.ToLowerInvariant() switch
    {
        ".txt" => "markdown+hard_line_breaks",
        ".md" => "markdown",
        ".html" => "html",
        ".docx" => "docx",
        ".rtf" => "rtf",
        ".odt" => "odt",
        _ => throw new NotSupportedException($"No Pandoc reader for '{ext}'.")
    };

    public static string Writer(string ext) => ext.ToLowerInvariant() switch
    {
        ".txt" => "plain",
        ".md" => "gfm",
        ".html" => "html5",
        ".docx" => "docx",
        ".rtf" => "rtf",
        ".odt" => "odt",
        _ => throw new NotSupportedException($"No Pandoc writer for '{ext}'.")
    };

    /// <param name="resourcePath">Directory of the source file, so relative images resolve.</param>
    /// <param name="extractMediaDir">Relative folder name for extracted images (md output only), or null.</param>
    public static IReadOnlyList<string> Build(string sourceExt, string targetExt, string inputPath, string outputPath,
        string resourcePath, string? extractMediaDir = null)
    {
        var args = new List<string>
        {
            "-f", Reader(sourceExt),
            "-t", Writer(targetExt),
            "-o", outputPath,
            "--resource-path", resourcePath,
        };

        switch (targetExt.ToLowerInvariant())
        {
            case ".html":
                AddSelfContainedHtml(args, sourceExt, Path.GetFileNameWithoutExtension(inputPath));
                break;
            case ".rtf":
                args.Add("--standalone");
                break;
            case ".md":
            case ".txt":
                args.Add("--wrap=none");
                break;
        }

        if (extractMediaDir is not null && targetExt.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--extract-media");
            args.Add(extractMediaDir);
        }

        args.Add(inputPath);
        return args;
    }

    /// <summary>Standalone embedded HTML. This is also the intermediate that PR 2 hands to wkhtmltopdf.</summary>
    public static IReadOnlyList<string> BuildHtmlIntermediate(string sourceExt, string inputPath, string outputHtml, string resourcePath)
        => Build(sourceExt, ".html", inputPath, outputHtml, resourcePath);

    private static void AddSelfContainedHtml(List<string> args, string sourceExt, string title)
    {
        args.Add("--standalone");
        // --embed-resources would make Pandoc try to fetch http(s) images of an HTML *source*,
        // which would break the "no network" rule. Only embed for sources that carry their own media.
        if (!sourceExt.Equals(".html", StringComparison.OrdinalIgnoreCase))
            args.Add("--embed-resources");
        args.Add("--metadata");
        args.Add($"pagetitle={title}");
    }

    public static IReadOnlyList<string> BuildWkhtmltopdf(string inputHtml, string outputPdf) => new[]
    {
        "--quiet",
        "--enable-local-file-access",
        "--disable-javascript",
        "--load-error-handling", "ignore",
        "--load-media-error-handling", "ignore",
        "--encoding", "utf-8",
        "--page-size", "A4",
        inputHtml,
        outputPdf,
    };
}
