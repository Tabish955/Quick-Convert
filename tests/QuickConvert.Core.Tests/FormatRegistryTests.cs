using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using Xunit;

namespace QuickConvert.Core.Tests;

/// <summary>
/// Phase-2 semantics: FormatRegistry delegates to ConversionMatrix. The matrix is the gate;
/// these tests pin the delegation behavior, not a hardcoded list.
/// </summary>
public class FormatRegistryTests
{
    [Theory]
    [InlineData(".png", FileCategory.Image)]
    [InlineData(".PNG", FileCategory.Image)]
    [InlineData("photo.jpeg", FileCategory.Image)]
    [InlineData("clip.mp4", FileCategory.Video)]
    [InlineData("song.mp3", FileCategory.Audio)]
    [InlineData("notes.txt", FileCategory.Document)]
    [InlineData("data.csv", FileCategory.Spreadsheet)]
    [InlineData("bundle.zip", FileCategory.Archive)]
    [InlineData("bundle.rar", FileCategory.Archive)]
    [InlineData("unknown.qqq", FileCategory.Unknown)]
    public void GetCategory_MapsExtensionToCategory(string input, FileCategory expected)
    {
        Assert.Equal(expected, FormatRegistry.GetCategory(input));
    }

    [Theory]
    [InlineData("png", ".png")]
    [InlineData(".PNG", ".png")]
    [InlineData("  .JpG  ", ".jpg")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeExtension_ProducesDotLowercase(string? input, string expected)
    {
        Assert.Equal(expected, FormatRegistry.NormalizeExtension(input));
    }

    [Fact]
    public void IsSupported_MatchesTheMatrix()
    {
        Assert.True(FormatRegistry.IsSupported(".jpg"));
        Assert.True(FormatRegistry.IsSupported("movie.mkv"));
        Assert.True(FormatRegistry.IsSupported("bundle.rar"));   // rar is a SOURCE
        Assert.False(FormatRegistry.IsSupported(".notaformat"));
    }

    [Fact]
    public void GetSupportedTargetFormats_DelegatesToTheMatrix()
    {
        // Spot-check a few rows instead of duplicating the table here.
        Assert.Equal([".png", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
            FormatRegistry.GetSupportedTargetFormats(".jpg"));
        Assert.Equal([".7z", ".tar", ".gz"], FormatRegistry.GetSupportedTargetFormats(".zip"));
        Assert.DoesNotContain(".rar", FormatRegistry.GetSupportedTargetFormats(".zip"));
    }

    [Fact]
    public void GetSupportedTargetFormats_UnknownExtension_ReturnsEmpty()
    {
        Assert.Empty(FormatRegistry.GetSupportedTargetFormats(".qqq"));
        Assert.Empty(FormatRegistry.GetSupportedTargetFormats(""));
    }

    [Fact]
    public void GetCommonTargetFormats_MultipleImages_EqualsSingleImageTargets()
    {
        var single = FormatRegistry.GetSupportedTargetFormats(".png");

        var common = FormatRegistry.GetCommonTargetFormats(["a.png", "b.png", "c.png"]);

        Assert.Equal(single, common);
    }

    [Fact]
    public void GetCommonTargetFormats_PreservesFirstFilesOrder()
    {
        var single = FormatRegistry.GetSupportedTargetFormats(".png");

        // Run it twice: a HashSet-backed implementation would be allowed to change order between runs,
        // which is what makes the pre-selected format in the popup jump around.
        var first = FormatRegistry.GetCommonTargetFormats(["a.png", "b.bmp"]);
        var second = FormatRegistry.GetCommonTargetFormats(["a.png", "b.bmp"]);

        Assert.Equal(first, second);
        Assert.Equal(single[0], first[0]);

        // When a file in the selection excludes single[0], the remaining common targets
        // must still follow the first file's order.
        var withJpg = FormatRegistry.GetCommonTargetFormats(["a.png", "b.jpg"]);
        Assert.Equal(single.First(t => !t.Equals(".jpg", StringComparison.OrdinalIgnoreCase)), withJpg[0]);
    }

    [Fact]
    public void GetCommonTargetFormats_DisjointCategories_ReturnsEmpty()
    {
        // Images can go to .pdf, archives cannot; archives can go to .7z, images cannot.
        var common = FormatRegistry.GetCommonTargetFormats(["photo.png", "bundle.zip"]);

        Assert.Empty(common);
    }

    [Fact]
    public void GetCommonTargetFormats_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(FormatRegistry.GetCommonTargetFormats([]));
        Assert.Empty(FormatRegistry.GetCommonTargetFormats(["", "   "]));
    }
}
