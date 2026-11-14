using System.Security.Cryptography;
using QuickConvert.Engines.Docs.Archives;
using QuickConvert.Engines.Docs.Tools;
using static QuickConvert.Engines.Docs.Tests.ZipFactory;

namespace QuickConvert.Engines.Docs.Tests;

public class ArchiveCompressorTests
{
    private static string MakeTree(TempDir t, string name = "tree", int files = 25)
    {
        var root = t.Sub(name);
        for (var i = 0; i < files; i++)
        {
            var dir = Path.Combine(root, "d" + (i % 4), "sub" + (i % 3));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"f{i}.bin"), Random(500 + i * 37, i));
        }
        File.WriteAllText(Path.Combine(root, "top.txt"), "top");
        return root;
    }

    private static Dictionary<string, string> Hashes(string root) =>
        ArchiveCompressor.EnumerateFiles(root).ToDictionary(
            f => Path.GetRelativePath(root, f).Replace('\\', '/'),
            f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    [InlineData(".gz")]
    public async Task Compress_then_extract_gives_identical_file_count_and_hashes(string target)
    {
        using var t = new TempDir();
        var tree = MakeTree(t);
        var c = new ArchiveCompressor(new FakeRunner(), () => null);

        var made = await c.CompressAsync(new CompressionRequest(tree, target, t.Sub("arc"), false), null, default);
        Assert.True(made.Success, made.Message);
        Assert.EndsWith(target == ".gz" ? "tree.tar.gz" : "tree" + target, made.OutputPath);

        var r = new ArchiveExtractor().Extract(made.OutputPath!, t.Sub("out"), null, default);
        Assert.True(r.Success, r.Message);

        var before = Hashes(tree);
        var after = Hashes(r.OutputDirectory!);
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before, after);
        Assert.Empty(Directory.GetDirectories(t.Sub("arc"), ".tmp_*"));
    }

    [Fact]
    public async Task Rar_is_never_a_target()
    {
        using var t = new TempDir();
        var c = new ArchiveCompressor(new FakeRunner(), () => "7za.exe");
        Assert.False(c.CanCompressTo(".rar"));
        var r = await c.CompressAsync(new CompressionRequest(MakeTree(t, files: 1), ".rar", t.Sub("o"), false), null, default);
        Assert.False(r.Success);
        Assert.Contains("RAR", r.Message);
        Assert.False(Directory.Exists(t.Sub("o")) && Directory.GetFileSystemEntries(t.Sub("o")).Length > 0);
    }

    [Fact]
    public async Task SevenZip_without_the_tool_is_hidden_and_fails_with_an_install_hint()
    {
        using var t = new TempDir();
        var c = new ArchiveCompressor(new FakeRunner(), () => null);
        Assert.False(c.CanCompressTo(".7z"));
        Assert.True(c.CanCompressTo(".zip"));
        var r = await c.CompressAsync(new CompressionRequest(MakeTree(t, files: 1), ".7z", t.Sub("o"), false), null, default);
        Assert.False(r.Success);
        Assert.Equal(ArchiveCompressor.SevenZipMissing, r.Message);
    }

    [Fact]
    public async Task SevenZip_is_invoked_with_safe_arguments_and_promoted()
    {
        using var t = new TempDir();
        var tree = MakeTree(t, files: 2);
        var runner = new FakeRunner();
        runner.OnRun = (_, a) => File.WriteAllBytes(a[^2], new byte[] { 1, 2, 3 }); // archive path is second to last, "*" is last
        var c = new ArchiveCompressor(runner, () => @"C:\t\7za.exe");

        var r = await c.CompressAsync(new CompressionRequest(tree, ".7z", t.Sub("o"), false), null, default);

        Assert.True(r.Success, r.Message);
        Assert.Equal(t.Sub("o", "tree.7z"), r.OutputPath);
        var args = runner.Calls[0].Args;
        Assert.Equal("a", args[0]);
        Assert.Contains("-t7z", args);
        Assert.Contains("-snl", args);   // store links as links, never follow them
        Assert.Equal("*", args[^1]);
        Assert.Empty(Directory.GetDirectories(t.Sub("o"), ".tmp_*"));
    }

    [Fact]
    public async Task SevenZip_failure_reports_stderr_and_leaves_nothing()
    {
        using var t = new TempDir();
        var runner = new FakeRunner { Behaviour = (_, _) => new ProcessResult(2, "Fatal error") };
        var c = new ArchiveCompressor(runner, () => @"C:\t\7za.exe");
        var r = await c.CompressAsync(new CompressionRequest(MakeTree(t, files: 1), ".7z", t.Sub("o"), false), null, default);
        Assert.False(r.Success);
        Assert.Contains("Fatal error", r.Message);
        Assert.Empty(Directory.GetFileSystemEntries(t.Sub("o")));
    }

    [Fact]
    public async Task Existing_archive_name_is_numbered()
    {
        using var t = new TempDir();
        var tree = MakeTree(t, files: 1);
        Directory.CreateDirectory(t.Sub("o")); File.WriteAllText(t.Sub("o", "tree.zip"), "mine");
        var c = new ArchiveCompressor(new FakeRunner(), () => null);
        var r = await c.CompressAsync(new CompressionRequest(tree, ".zip", t.Sub("o"), false), null, default);
        Assert.Equal(t.Sub("o", "tree (1).zip"), r.OutputPath);
        Assert.Equal("mine", File.ReadAllText(t.Sub("o", "tree.zip")));
    }

    [Fact]
    public async Task Cancellation_leaves_no_partial_archive()
    {
        using var t = new TempDir();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var c = new ArchiveCompressor(new FakeRunner(), () => null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.CompressAsync(new CompressionRequest(MakeTree(t, files: 3), ".zip", t.Sub("o"), false), null, cts.Token));
        Assert.Empty(Directory.GetFileSystemEntries(t.Sub("o")));
    }

    [Fact]
    public void Reparse_points_are_skipped_when_enumerating()
    {
        // The enumerator consults FileAttributes.ReparsePoint; on a plain tree it must list everything exactly once.
        using var t = new TempDir();
        var tree = MakeTree(t, files: 6);
        var listed = ArchiveCompressor.EnumerateFiles(tree).ToList();
        Assert.Equal(7, listed.Count);
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }

    [SkippableFact]
    public async Task Real_7za_zip_to_7z_to_extracted_round_trip()
    {
        var sevenZip = ToolVersions.SevenZipLocator().Locate();
        Skip.If(sevenZip is null, "7za not installed (run tools/Get-7za.ps1)");

        using var t = new TempDir();
        var tree = MakeTree(t, files: 40);
        var c = new ArchiveCompressor(new ToolProcessRunner(), () => sevenZip);

        var made = await c.CompressAsync(new CompressionRequest(tree, ".7z", t.Sub("arc"), false), null, default);
        Assert.True(made.Success, made.Message);
        var r = new ArchiveExtractor().Extract(made.OutputPath!, t.Sub("out"), null, default);
        Assert.True(r.Success, r.Message);
        Assert.Equal(Hashes(tree), Hashes(r.OutputDirectory!));
    }
}
