using QuickConvert.Core.IO;

namespace QuickConvert.Engines.Docs.Tests;

public class PathGuardTests
{
    [Theory]
    [InlineData("a.txt", "a.txt")]
    [InlineData("dir/a.txt", "dir/a.txt")]
    [InlineData("dir\\sub\\a.txt", "dir/sub/a.txt")]
    [InlineData("./a.txt", "a.txt")]
    [InlineData("a/../b.txt", "b.txt")]
    [InlineData("dir/", "dir")]
    [InlineData("photo (1).png", "photo (1).png")]
    public void Valid_entries_are_normalised(string key, string expected)
    {
        var r = ArchivePathGuard.Validate(key);
        Assert.True(r.IsValid, r.Message);
        Assert.Equal(expected, r.RelativePath);
    }

    [Theory]
    [InlineData("/etc/passwd", ArchivePathError.Rooted)]
    [InlineData("\\\\server\\share\\x.txt", ArchivePathError.Rooted)]
    [InlineData("\\windows\\system32\\x.dll", ArchivePathError.Rooted)]
    [InlineData("C:\\Windows\\win.ini", ArchivePathError.DriveLetter)]
    [InlineData("c:/x.txt", ArchivePathError.DriveLetter)]
    [InlineData("C:relative.txt", ArchivePathError.DriveLetter)]
    [InlineData("../evil.txt", ArchivePathError.Escapes)]
    [InlineData("..\\evil.txt", ArchivePathError.Escapes)]
    [InlineData("a/../../evil.txt", ArchivePathError.Escapes)]
    [InlineData("a/b/../../../evil.txt", ArchivePathError.Escapes)]
    [InlineData("file.txt:stream", ArchivePathError.InvalidCharacters)]
    [InlineData("bad|name.txt", ArchivePathError.InvalidCharacters)]
    [InlineData("what?.txt", ArchivePathError.InvalidCharacters)]
    [InlineData("trailing.", ArchivePathError.InvalidCharacters)]
    [InlineData("CON", ArchivePathError.ReservedName)]
    [InlineData("dir/aux.txt", ArchivePathError.ReservedName)]
    [InlineData("nul.tar.gz", ArchivePathError.ReservedName)]
    [InlineData("", ArchivePathError.Empty)]
    [InlineData("   ", ArchivePathError.Empty)]
    [InlineData("./", ArchivePathError.Empty)]
    public void Hostile_entries_are_rejected(string key, ArchivePathError expected)
    {
        var r = ArchivePathGuard.Validate(key);
        Assert.False(r.IsValid);
        Assert.Equal(expected, r.Error);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
    }

    [Fact]
    public void Null_and_nul_characters_are_rejected()
    {
        Assert.False(ArchivePathGuard.Validate(null).IsValid);
        Assert.False(ArchivePathGuard.Validate("a\0b.txt").IsValid);
    }

    [Fact]
    public void Combine_stays_inside_root_and_throws_otherwise()
    {
        using var t = new TempDir();
        var ok = ArchivePathGuard.Combine(t.Path, "a/b.txt");
        Assert.StartsWith(Path.GetFullPath(t.Path), ok);
        Assert.Throws<InvalidOperationException>(() => ArchivePathGuard.Combine(t.Path, "../x.txt"));
    }

    [Fact]
    public void Combine_does_not_accept_sibling_with_same_prefix()
    {
        using var t = new TempDir();
        var root = Path.Combine(t.Path, "out");
        Directory.CreateDirectory(root);
        Assert.Throws<InvalidOperationException>(() => ArchivePathGuard.Combine(root, "../out-evil/x.txt"));
    }
}
