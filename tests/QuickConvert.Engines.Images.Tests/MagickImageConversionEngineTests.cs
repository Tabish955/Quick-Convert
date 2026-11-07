using ImageMagick;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using Xunit;

namespace QuickConvert.Engines.Images.Tests;

public class MagickImageConversionEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qc-img-" + Guid.NewGuid().ToString("N"));

    public MagickImageConversionEngineTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch { /* temp-dir cleanup is best effort */ }
    }

    private string MakeSource(string name = "photo.png", uint w = 64, uint h = 48)
    {
        var path = Path.Combine(_dir, name);
        using var img = new MagickImage(new MagickColor(200, 30, 30), w, h);
        img.Write(path);
        return path;
    }

    [Fact]
    public async Task PngToJpg_ProducesRealJpeg()
    {
        var engine = new MagickImageConversionEngine();
        var result = await engine.ConvertAsync(
            ConversionJob.FromFilePath(MakeSource(), ".jpg"),
            new ConversionOptions { TargetExtension = ".jpg" },
            progress: null, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        var bytes = await File.ReadAllBytesAsync(result.OutputFilePath);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);   // JPEG SOI marker
    }

    [Fact]
    public async Task PngToWebp_StartsWithRiffWebp()
    {
        var engine = new MagickImageConversionEngine();
        var result = await engine.ConvertAsync(
            ConversionJob.FromFilePath(MakeSource(), ".webp"),
            new ConversionOptions { TargetExtension = ".webp" },
            progress: null, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        var bytes = await File.ReadAllBytesAsync(result.OutputFilePath);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes[8..12]));
    }

    [Fact]
    public async Task ExistingOutput_GetsNumberedSuffixNotOverwrite()
    {
        var src = MakeSource();
        await File.WriteAllTextAsync(Path.Combine(_dir, "photo.jpg"), "old");

        var engine = new MagickImageConversionEngine();
        var result = await engine.ConvertAsync(
            ConversionJob.FromFilePath(src, ".jpg"),
            new ConversionOptions { TargetExtension = ".jpg" },
            progress: null, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("photo (1).jpg", Path.GetFileName(result.OutputFilePath));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(_dir, "photo.jpg")));
    }

    [Fact]
    public async Task CancelledBeforeStart_LeavesNothingBehind_AndDoesNotThrow()
    {
        var engine = new MagickImageConversionEngine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await engine.ConvertAsync(
            ConversionJob.FromFilePath(MakeSource(), ".jpg"),
            new ConversionOptions { TargetExtension = ".jpg" },
            progress: null, cts.Token);

        Assert.False(result.Success);
        Assert.Empty(Directory.GetFiles(_dir, "*.jpg"));
    }

    [Fact]
    public async Task UnreadableSource_ReturnsFailedResult_NotException()
    {
        var engine = new MagickImageConversionEngine();
        var job = ConversionJob.FromFilePath(Path.Combine(_dir, "missing.png"), ".jpg");

        var result = await engine.ConvertAsync(
            job, new ConversionOptions { TargetExtension = ".jpg" },
            progress: null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ExifOrientation6_IsAppliedOnWrite()
    {
        // 100x200 portrait photo tagged as rotated → AutoOrient must swap dimensions.
        var src = Path.Combine(_dir, "phone.jpg");
        using (var img = new MagickImage(new MagickColor(10, 200, 10), 100, 200))
        {
            var profile = new ExifProfile();
            profile.SetValue(ExifTag.Orientation, (ushort)6);
            img.SetProfile(profile);
            img.Orientation = OrientationType.RightTop;
            img.Write(src);
        }

        var engine = new MagickImageConversionEngine();
        var result = await engine.ConvertAsync(
            ConversionJob.FromFilePath(src, ".png"),
            new ConversionOptions { TargetExtension = ".png" },
            progress: null, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        using var check = new MagickImage(result.OutputFilePath);
        Assert.Equal(200u, check.Width);
        Assert.Equal(100u, check.Height);
    }

    [Fact]
    public void CanConvert_ExcludesRarAndArbitraryTargets()
    {
        var engine = new MagickImageConversionEngine();

        Assert.False(engine.CanConvert(".rar", ".zip"));
        Assert.False(engine.CanConvert(".jpg", ".rar"));   // licence guard
        Assert.False(engine.CanConvert(".jpg", ".mkv"));   // not an image target
        Assert.True(engine.CanConvert(".gif", ".pdf"));
    }

    [Fact]
    public async Task QualityOption_IsAppliedToJpegEncode()
    {
        var src = MakeSource();
        var engine = new MagickImageConversionEngine();

        var low = await engine.ConvertAsync(
            ConversionJob.FromFilePath(src, ".jpg"),
            new ConversionOptions { TargetExtension = ".jpg", Quality = 5 },
            progress: null, CancellationToken.None);
        var high = await engine.ConvertAsync(
            ConversionJob.FromFilePath(src, ".jpg"),
            new ConversionOptions { TargetExtension = ".jpg", Quality = 95, OverwriteExisting = false },
            progress: null, CancellationToken.None);

        Assert.True(low.Success, low.ErrorMessage);
        Assert.True(high.Success, high.ErrorMessage);
        Assert.True(new FileInfo(low.OutputFilePath).Length < new FileInfo(high.OutputFilePath).Length);
    }
}
