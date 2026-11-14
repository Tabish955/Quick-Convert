using System.Security.Cryptography;
using QuickConvert.Engines.Docs.Archives;
using static QuickConvert.Engines.Docs.Tests.ZipFactory;

namespace QuickConvert.Engines.Docs.Tests;

public class ArchiveExtractorTests
{
    private readonly ArchiveExtractor _x = new();

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static void AssertNothingLeft(string outDir)
    {
        Assert.Empty(Directory.GetDirectories(outDir, ".tmp-extract-*"));
    }

    // ---------- happy path, collisions, structure ----------

    [Fact]
    public void Zip_extracts_into_a_folder_named_after_the_archive_preserving_structure()
    {
        using var t = new TempDir();
        var zip = MakeZip(t.Sub("pack.zip"),
            ("readme.txt", Bytes("hi")), ("docs/a.txt", Bytes("A")), ("docs/deep/b.txt", Bytes("B")), ("emptydir/", Array.Empty<byte>()));
        var outDir = t.Sub("out");

        var r = _x.Extract(zip, outDir, null, default);

        Assert.True(r.Success, r.Message);
        Assert.Equal(Path.Combine(outDir, "pack"), r.OutputDirectory);
        Assert.Equal("hi", File.ReadAllText(Path.Combine(r.OutputDirectory!, "readme.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(r.OutputDirectory!, "docs", "deep", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(r.OutputDirectory!, "emptydir")));
        Assert.Equal(3, r.EntriesExtracted);
        AssertNothingLeft(outDir);
    }

    [Fact]
    public void Two_entries_with_the_same_name_are_numbered_and_folders_stay_intact()
    {
        using var t = new TempDir();
        var zip = MakeZip(t.Sub("c.zip"), ("img/Photo.png", Bytes("1")), ("img/photo.png", Bytes("2")), ("img/PHOTO.PNG", Bytes("3")));
        var r = _x.Extract(zip, t.Sub("out"), null, default);

        Assert.True(r.Success, r.Message);
        var files = Directory.GetFiles(Path.Combine(r.OutputDirectory!, "img")).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(3, files.Length);
        Assert.Contains("Photo.png", files);
        Assert.Contains("photo (1).png", files);
        Assert.Contains("PHOTO (2).PNG", files);
    }

    [Fact]
    public void Existing_destination_folder_is_never_merged_or_deleted()
    {
        using var t = new TempDir();
        var outDir = t.Sub("out");
        Directory.CreateDirectory(Path.Combine(outDir, "pack"));
        File.WriteAllText(Path.Combine(outDir, "pack", "mine.txt"), "mine");

        var zip = MakeZip(t.Sub("pack.zip"), ("x.txt", Bytes("x")));
        var r = _x.Extract(zip, outDir, null, default);

        Assert.True(r.Success, r.Message);
        Assert.Equal(Path.Combine(outDir, "pack (1)"), r.OutputDirectory);
        Assert.True(File.Exists(Path.Combine(outDir, "pack", "mine.txt")));
        Assert.False(File.Exists(Path.Combine(outDir, "pack", "x.txt")));
        Assert.True(File.Exists(Path.Combine(outDir, "pack (1)", "x.txt")));
    }

    [Fact]
    public void Progress_is_reported_per_entry_not_invented()
    {
        using var t = new TempDir();
        var zip = MakeZip(t.Sub("p.zip"), ("a.txt", Bytes("a")), ("b/b.txt", Bytes("b")), ("c.txt", Bytes("c")));
        var seen = new List<ExtractionProgress>();
        var r = _x.Extract(zip, t.Sub("out"), new SyncProgress(seen), default);

        Assert.True(r.Success, r.Message);
        Assert.Equal(new[] { 1, 2, 3 }, seen.Select(p => p.EntriesDone).ToArray());
        Assert.All(seen, p => Assert.Equal(3, p.EntriesTotal));
    }

    [Fact]
    public void Tar_gz_made_by_the_compressor_extracts_back()
    {
        using var t = new TempDir();
        var src = t.Sub("src"); Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "A"); File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "B");

        var c = new ArchiveCompressor(new FakeRunner(), () => null);
        var made = c.CompressAsync(new CompressionRequest(src, ".gz", t.Sub("arc"), false), null, default).GetAwaiter().GetResult();
        Assert.True(made.Success, made.Message);
        Assert.EndsWith("src.tar.gz", made.OutputPath);

        var r = _x.Extract(made.OutputPath!, t.Sub("out"), null, default);
        Assert.True(r.Success, r.Message);
        Assert.Equal(Path.Combine(t.Sub("out"), "src"), r.OutputDirectory);
        Assert.Equal("B", File.ReadAllText(Path.Combine(r.OutputDirectory!, "sub", "b.txt")));
    }

    [Fact]
    public void Plain_gz_extracts_a_single_file()
    {
        using var t = new TempDir();
        var gz = t.Sub("notes.txt.gz");
        using (var fs = File.Create(gz))
        using (var g = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
            g.Write(Bytes("hello gz"));

        var r = _x.Extract(gz, t.Sub("out"), null, default);
        Assert.True(r.Success, r.Message);
        Assert.Equal("hello gz", File.ReadAllText(Path.Combine(r.OutputDirectory!, "notes.txt")));
    }

    // ---------- zip slip ----------

    [Fact]
    public void Zip_slip_fails_the_whole_job_and_writes_nothing()
    {
        using var t = new TempDir();
        var zip = MakeZip(t.Sub("evil.zip"), ("fine.txt", Bytes("ok")), ("../escaped.txt", Bytes("pwn")));
        var outDir = t.Sub("out");

        var r = _x.Extract(zip, outDir, null, default);

        Assert.False(r.Success);
        Assert.Equal(ArchiveFailure.UnsafePath, r.Failure);
        Assert.Contains("rejected for safety", r.Message);
        Assert.False(File.Exists(t.Sub("escaped.txt")));
        Assert.False(File.Exists(Path.Combine(outDir, "escaped.txt")));
        Assert.False(Directory.Exists(Path.Combine(outDir, "evil")));   // not even the benign entry was promoted
        AssertNothingLeft(outDir);
    }

    [Theory]
    [InlineData("/abs/evil.txt")]
    [InlineData("C:\\Windows\\evil.txt")]
    [InlineData("a/b/../../../evil.txt")]
    [InlineData("..\\evil.txt")]
    public void Other_hostile_entry_names_are_rejected(string name)
    {
        using var t = new TempDir();
        var zip = MakeZip(t.Sub("h.zip"), (name, Bytes("x")));
        var r = _x.Extract(zip, t.Sub("out"), null, default);
        Assert.False(r.Success);
        Assert.Equal(ArchiveFailure.UnsafePath, r.Failure);
        Assert.False(Directory.Exists(t.Sub("out", "h")));
        AssertNothingLeft(t.Sub("out"));
    }

    // ---------- corrupt / truncated / unsupported ----------

    [Fact]
    public void Truncated_zip_gives_a_clear_message_and_leaves_no_folder()
    {
        using var t = new TempDir();
        var good = MakeZip(t.Sub("good.zip"), ("a.bin", Random(200_000, 1)), ("b.bin", Random(200_000, 2)));
        var bytes = File.ReadAllBytes(good);
        var bad = t.Sub("truncated.zip");
        File.WriteAllBytes(bad, bytes[..(bytes.Length * 6 / 10)]); // generated here, never committed

        var outDir = t.Sub("out");
        var r = _x.Extract(bad, outDir, null, default);

        Assert.False(r.Success);
        Assert.Equal(ArchiveFailure.Corrupt, r.Failure);
        Assert.Contains("damaged or incomplete", r.Message);
        Assert.False(Directory.Exists(Path.Combine(outDir, "truncated")));
        AssertNothingLeft(outDir);
    }

    [Fact]
    public void Flipped_bytes_in_the_middle_of_a_zip_are_detected()
    {
        using var t = new TempDir();
        var good = MakeZip(t.Sub("good.zip"), ("a.bin", Random(300_000, 3)));
        var bytes = File.ReadAllBytes(good);
        for (var i = bytes.Length / 3; i < bytes.Length / 3 + 64; i++) bytes[i] ^= 0xFF;
        var bad = t.Sub("flipped.zip"); File.WriteAllBytes(bad, bytes);

        var outDir = t.Sub("out");
        var r = _x.Extract(bad, outDir, null, default);

        // Stored (incompressible) data may not fail inflate, but the size/CRC check must still catch it
        // OR SharpCompress throws. Either way: never a silent success with a half-trusted folder.
        if (!r.Success)
        {
            Assert.Equal(ArchiveFailure.Corrupt, r.Failure);
            Assert.False(Directory.Exists(Path.Combine(outDir, "flipped")));
        }
        AssertNothingLeft(outDir);
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    [InlineData(".rar")]
    [InlineData(".tar")]
    [InlineData(".gz")]
    public void Garbage_with_an_archive_extension_is_corrupt_not_an_exception(string ext)
    {
        using var t = new TempDir();
        var f = t.Sub("junk" + ext);
        File.WriteAllBytes(f, Random(4096, 42));
        var r = _x.Extract(f, t.Sub("out"), null, default);
        Assert.False(r.Success);
        Assert.Equal(ArchiveFailure.Corrupt, r.Failure);
        Assert.False(Directory.Exists(t.Sub("out", "junk")));
        AssertNothingLeft(t.Sub("out"));
    }

    [Fact]
    public void Unsupported_extension_and_missing_file_fail_cleanly()
    {
        using var t = new TempDir();
        var iso = t.Sub("x.iso"); File.WriteAllText(iso, "x");
        Assert.Equal(ArchiveFailure.Unsupported, _x.Extract(iso, t.Sub("o"), null, default).Failure);
        Assert.False(_x.Extract(t.Sub("ghost.zip"), t.Sub("o"), null, default).Success);
    }

    [Fact]
    public void Cancellation_throws_and_leaves_no_temp_folder()
    {
        using var t = new TempDir();
        var zip = MakeZip(t.Sub("c.zip"), ("a.txt", Bytes("a")));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => _x.Extract(zip, t.Sub("out"), null, cts.Token));
        AssertNothingLeft(t.Sub("out"));
        Assert.False(Directory.Exists(t.Sub("out", "c")));
    }

    // ---------- exception classification (what SharpCompress throws never reaches the user) ----------

    [Fact]
    public void Classify_maps_exceptions_to_plain_categories()
    {
        Assert.Equal(ArchiveFailure.Encrypted, ArchiveExtractor.Classify(new CryptographicException("x")));
        Assert.Equal(ArchiveFailure.Encrypted, ArchiveExtractor.Classify(new InvalidOperationException("Encrypted file, password required")));
        Assert.Equal(ArchiveFailure.Encrypted, ArchiveExtractor.Classify(new IOException("outer", new Exception("a password is needed"))));
        Assert.Equal(ArchiveFailure.Corrupt, ArchiveExtractor.Classify(new EndOfStreamException()));
        Assert.Equal(ArchiveFailure.Corrupt, ArchiveExtractor.Classify(new InvalidDataException("bad crc")));
    }

    [Theory]
    [InlineData("backup.tar.gz", "backup")]
    [InlineData("photos.zip", "photos")]
    [InlineData("archive.v2.7z", "archive.v2")]
    [InlineData("data.TAR", "data")]
    public void Folder_names_are_derived_from_the_archive_name(string file, string expected) =>
        Assert.Equal(expected, ArchiveExtractor.FolderNameFor(Path.Combine("c:\\x", file)));

    // ---------- format validation (RAR read-only) ----------

    [Theory]
    [InlineData(".zip", true)] [InlineData(".7z", true)] [InlineData(".tar", true)] [InlineData(".gz", true)]
    [InlineData(".rar", false)] [InlineData(".iso", false)] [InlineData(".tgz", false)]
    public void Only_roadmap_formats_are_targets_and_rar_never_is(string ext, bool target)
    {
        Assert.Equal(target, ArchiveFormats.IsValidTarget(ext));
        Assert.DoesNotContain(".rar", ArchiveFormats.CompressionTargets);
    }

    [Fact]
    public void Rar_is_extractable_but_not_a_source_of_other_targets()
    {
        Assert.True(ArchiveFormats.IsExtractable(".RAR"));
        Assert.True(ArchiveFormats.IsExtractable(".zip"));
        Assert.False(ArchiveFormats.IsExtractable(".iso"));
    }

    // ---------- fixtures (self-skip when absent; see tests/.../fixtures/README.md) ----------

    [SkippableTheory]
    [InlineData("sample-rar4.rar")]
    [InlineData("sample-rar5.rar")]
    public void Rar4_and_Rar5_samples_extract_hello_txt(string fixture)
    {
        var path = FixturePath(fixture);
        Skip.IfNot(File.Exists(path), $"fixture {fixture} not present");
        using var t = new TempDir();
        var r = _x.Extract(path, t.Sub("out"), null, default);
        Assert.True(r.Success, $"{fixture}: {r.Message}");
        Assert.True(File.Exists(Path.Combine(r.OutputDirectory!, "hello.txt")));
    }

    [SkippableTheory]
    [InlineData("encrypted.zip")]
    [InlineData("encrypted.7z")]
    [InlineData("encrypted-headers.rar")]
    public void Encrypted_archives_are_reported_plainly_and_nothing_is_written(string fixture)
    {
        var path = FixturePath(fixture);
        Skip.IfNot(File.Exists(path), $"fixture {fixture} not present");
        using var t = new TempDir();
        var r = _x.Extract(path, t.Sub("out"), null, default);
        Assert.False(r.Success);
        Assert.Equal(ArchiveFailure.Encrypted, r.Failure);
        Assert.Contains("password-protected", r.Message);
        Assert.Empty(Directory.GetDirectories(t.Sub("out")));
    }

    private sealed class SyncProgress(List<ExtractionProgress> sink) : IProgress<ExtractionProgress>
    {
        public void Report(ExtractionProgress value) => sink.Add(value);
    }
}
