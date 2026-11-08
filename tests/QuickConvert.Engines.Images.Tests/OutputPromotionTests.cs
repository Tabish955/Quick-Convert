using Xunit;

namespace QuickConvert.Engines.Images.Tests;

public class OutputPromotionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qc-promote-" + Guid.NewGuid().ToString("N"));

    public OutputPromotionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { /* temp-dir cleanup is best effort */ }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Promote(string temp, string firstChoice, bool overwrite = false) =>
        OutputPromotion.Promote(temp, _dir, "photo", ".webp", firstChoice, overwrite);

    [Fact]
    public void FreeName_IsUsedAsIs()
    {
        var temp = Write(".tmp_a.webp", "new");
        var wanted = Path.Combine(_dir, "photo.webp");

        var result = Promote(temp, wanted);

        Assert.Equal(wanted, result);
        Assert.Equal("new", File.ReadAllText(wanted));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void NameTakenAfterItWasChosen_IsNeverOverwritten_AndTheNextFreeNameIsUsed()
    {
        var wanted = Write("photo.webp", "first job's output");   // appeared after the name was picked
        var temp = Write(".tmp_b.webp", "second job's output");

        var result = Promote(temp, wanted);

        Assert.Equal(Path.Combine(_dir, "photo (1).webp"), result);
        Assert.Equal("first job's output", File.ReadAllText(wanted));
        Assert.Equal("second job's output", File.ReadAllText(result));
    }

    [Fact]
    public void SeveralClashes_SkipEveryTakenName()
    {
        var wanted = Write("photo.webp", "0");
        Write("photo (1).webp", "1");
        Write("photo (2).webp", "2");
        var temp = Write(".tmp_c.webp", "new");

        var result = Promote(temp, wanted);

        Assert.Equal(Path.Combine(_dir, "photo (3).webp"), result);
        Assert.Equal("0", File.ReadAllText(wanted));
        Assert.Equal("new", File.ReadAllText(result));
    }

    [Fact]
    public void OverwriteRequested_ReplacesTheExistingFile()
    {
        var wanted = Write("photo.webp", "old");
        var temp = Write(".tmp_d.webp", "new");

        var result = Promote(temp, wanted, overwrite: true);

        Assert.Equal(wanted, result);
        Assert.Equal("new", File.ReadAllText(wanted));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void MissingTempFile_IsReportedAsAnError_NotAsANameClash()
    {
        var wanted = Path.Combine(_dir, "photo.webp");

        Assert.ThrowsAny<IOException>(() => Promote(Path.Combine(_dir, "does-not-exist.tmp"), wanted));
        Assert.False(File.Exists(wanted));
    }
}
