using QuickConvert.Engines.Docs.Documents;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs.Tests;

public class DocumentConverterTests
{
    private const string Pandoc = @"C:\t\pandoc.exe";

    private static FakeRunner WritingRunner()
    {
        var r = new FakeRunner();
        r.OnRun = (_, a) => File.WriteAllText(FakeRunner.OutputOf(a), "out");
        return r;
    }

    private static string Source(TempDir t, string name = "report.docx")
    {
        var p = t.Sub(name); File.WriteAllText(p, "x"); return p;
    }

    // ---- argument builder ----

    [Theory]
    [InlineData(".txt", "markdown+hard_line_breaks")]
    [InlineData(".md", "markdown")]
    [InlineData(".html", "html")]
    [InlineData(".docx", "docx")]
    [InlineData(".rtf", "rtf")]
    [InlineData(".odt", "odt")]
    public void Readers_map(string ext, string reader) => Assert.Equal(reader, PandocArgumentBuilder.Reader(ext));

    [Theory]
    [InlineData(".txt", "plain")]
    [InlineData(".md", "gfm")]
    [InlineData(".html", "html5")]
    [InlineData(".docx", "docx")]
    [InlineData(".rtf", "rtf")]
    [InlineData(".odt", "odt")]
    public void Writers_map(string ext, string writer) => Assert.Equal(writer, PandocArgumentBuilder.Writer(ext));

    [Fact]
    public void Html_from_docx_is_standalone_and_embedded()
    {
        var a = PandocArgumentBuilder.Build(".docx", ".html", @"C:\in\r.docx", @"C:\o\out.html", @"C:\in");
        Assert.Contains("--standalone", a);
        Assert.Contains("--embed-resources", a);
        Assert.Contains("pagetitle=r", a);
        Assert.Equal(@"C:\in\r.docx", a[^1]);
        Assert.Equal(@"C:\in", a[a.ToList().IndexOf("--resource-path") + 1]);
    }

    [Fact]
    public void Html_source_never_asks_pandoc_to_fetch_remote_resources()
    {
        var a = PandocArgumentBuilder.Build(".html", ".docx", "in.html", "out.docx", ".");
        Assert.DoesNotContain("--embed-resources", a);
        var b = PandocArgumentBuilder.BuildHtmlIntermediate(".md", "in.md", "o.html", ".");
        Assert.Contains("--embed-resources", b);
    }

    [Fact]
    public void Markdown_gets_extract_media_only_when_requested()
    {
        var withMedia = PandocArgumentBuilder.Build(".docx", ".md", "i.docx", "o.md", ".", "r_media");
        Assert.Equal("r_media", withMedia[withMedia.ToList().IndexOf("--extract-media") + 1]);
        Assert.DoesNotContain("--extract-media", PandocArgumentBuilder.Build(".docx", ".txt", "i.docx", "o.txt", ".", "x"));
        Assert.Contains("--wrap=none", withMedia);
    }

    // ---- pair gating ----

    [Fact]
    public void Pair_matrix_excludes_pdf_source_and_same_format()
    {
        Assert.False(DocumentFormats.IsPandocPair(".pdf", ".txt"));
        Assert.False(DocumentFormats.IsPandocPair(".docx", ".docx"));
        Assert.False(DocumentFormats.IsPandocPair(".docx", ".pdf")); // pdf goes through the PDF path
        Assert.True(DocumentFormats.IsPdfPair(".docx", ".pdf"));
        Assert.False(DocumentFormats.IsPdfPair(".pdf", ".pdf"));
    }

    [Fact]
    public void CanConvert_hides_targets_when_pandoc_is_missing()
    {
        var none = new DocumentConverter(new FakeRunner(), () => null);
        Assert.False(none.CanConvert(".docx", ".md"));

        var pandocOnly = new DocumentConverter(new FakeRunner(), () => Pandoc);
        Assert.True(pandocOnly.CanConvert(".docx", ".md"));
    }

    // ---- conversion ----

    [Fact]
    public async Task Missing_pandoc_is_a_clean_failure_with_install_hint()
    {
        using var t = new TempDir();
        var c = new DocumentConverter(new FakeRunner(), () => null);
        var r = await c.ConvertAsync(new DocumentRequest(Source(t), ".md", t.Path, false), null, default);
        Assert.False(r.Success);
        Assert.Equal(DocumentConverter.PandocMissing, r.Message);
    }

    [Fact]
    public async Task Successful_conversion_promotes_and_leaves_no_temp()
    {
        using var t = new TempDir();
        var runner = WritingRunner();
        var c = new DocumentConverter(runner, () => Pandoc);
        var reported = new List<int>();
        var r = await c.ConvertAsync(new DocumentRequest(Source(t, "notes.rtf"), ".txt", t.Path, false),
            new SyncProgress(reported), default);

        Assert.True(r.Success, r.Message);
        Assert.Equal(t.Sub("notes.txt"), r.OutputPath);
        Assert.True(File.Exists(r.OutputPath));
        Assert.Empty(Directory.GetDirectories(t.Path, ".tmp_*"));
        Assert.Equal(100, reported[^1]);
        Assert.Contains(95, reported);
        Assert.True(reported.IndexOf(100) > reported.IndexOf(95)); // 100 strictly after promotion
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Existing_output_is_numbered_not_overwritten()
    {
        using var t = new TempDir();
        File.WriteAllText(t.Sub("notes.txt"), "mine");
        var c = new DocumentConverter(WritingRunner(), () => Pandoc);
        var r = await c.ConvertAsync(new DocumentRequest(Source(t, "notes.rtf"), ".txt", t.Path, false), null, default);
        Assert.Equal(t.Sub("notes (1).txt"), r.OutputPath);
        Assert.Equal("mine", File.ReadAllText(t.Sub("notes.txt")));
    }

    [Fact]
    public async Task Nonzero_exit_reports_stderr_and_cleans_up()
    {
        using var t = new TempDir();
        var runner = new FakeRunner { Behaviour = (_, _) => new ProcessResult(64, "pandoc: unknown reader") };
        var c = new DocumentConverter(runner, () => Pandoc);
        var r = await c.ConvertAsync(new DocumentRequest(Source(t), ".md", t.Path, false), null, default);
        Assert.False(r.Success);
        Assert.Contains("unknown reader", r.Message);
        Assert.Empty(Directory.GetDirectories(t.Path, ".tmp_*"));
        Assert.False(File.Exists(t.Sub("report.md")));
    }

    [Fact]
    public async Task Cancellation_propagates_and_purges_temp()
    {
        using var t = new TempDir();
        using var cts = new CancellationTokenSource();
        var runner = new FakeRunner();
        runner.OnRun = (_, a) => { File.WriteAllText(FakeRunner.OutputOf(a), "x"); cts.Cancel(); };
        var c = new DocumentConverter(runner, () => Pandoc);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.ConvertAsync(new DocumentRequest(Source(t), ".md", t.Path, false), null, cts.Token));
        Assert.Empty(Directory.GetDirectories(t.Path, ".tmp_*"));
    }

    [Fact]
    public async Task Docx_to_md_extracts_media_into_a_numbered_sibling_folder()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Sub("report_media")); // already taken
        var runner = new FakeRunner();
        runner.OnRun = (_, a) =>
        {
            File.WriteAllText(FakeRunner.OutputOf(a), "![](x)");
            var media = a[Array.IndexOf(a, "--extract-media") + 1];
            var wd = Path.GetDirectoryName(FakeRunner.OutputOf(a))!;
            Directory.CreateDirectory(Path.Combine(wd, media, "media"));
            File.WriteAllText(Path.Combine(wd, media, "media", "image1.png"), "png");
        };
        var r = await new DocumentConverter(runner, () => Pandoc)
            .ConvertAsync(new DocumentRequest(Source(t), ".md", t.Path, false), null, default);

        Assert.True(r.Success, r.Message);
        Assert.True(File.Exists(t.Sub("report_media (1)", "media", "image1.png")));
        Assert.Equal("report_media (1)", runner.Calls[0].Args[Array.IndexOf(runner.Calls[0].Args, "--extract-media") + 1]);
    }

    [Fact]
    public async Task Unsupported_pair_and_missing_source_fail_cleanly()
    {
        using var t = new TempDir();
        var c = new DocumentConverter(new FakeRunner(), () => Pandoc);
        Assert.False((await c.ConvertAsync(new DocumentRequest(Source(t, "a.pdf"), ".txt", t.Path, false), null, default)).Success);
        Assert.False((await c.ConvertAsync(new DocumentRequest(t.Sub("ghost.docx"), ".md", t.Path, false), null, default)).Success);
    }

    private sealed class SyncProgress(List<int> sink) : IProgress<int>
    {
        public void Report(int value) => sink.Add(value);
    }
}
