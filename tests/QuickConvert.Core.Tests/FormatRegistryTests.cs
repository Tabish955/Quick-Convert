using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using Xunit;

namespace QuickConvert.Core.Tests;

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
    public void IsSupported_MatchesTheRegistry()
    {
        Assert.True(FormatRegistry.IsSupported(".jpg"));
        Assert.True(FormatRegistry.IsSupported("movie.mkv"));
        Assert.False(FormatRegistry.IsSupported(".notaformat"));
    }

    [Fact]
    public void GetSupportedTargetFormats_NeverIncludesTheSourceItself()
    {
        foreach (var source in new[] { ".png", ".jpg", ".mp4", ".mp3", ".txt", ".csv", ".zip" })
        {
            var targets = FormatRegistry.GetSupportedTargetFormats(source);
            Assert.DoesNotContain(source, targets, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GetSupportedTargetFormats_IsNotEmpty_ForKnownFormats()
    {
        foreach (var source in new[] { ".png", ".jpg", ".mp4", ".mp3", ".txt", ".zip" })
        {
            Assert.NotEmpty(FormatRegistry.GetSupportedTargetFormats(source));
        }
    }

    [Fact]
    public void GetSupportedTargetFormats_UnknownExtension_ReturnsEmpty()
    {
        Assert.Empty(FormatRegistry.GetSupportedTargetFormats(".qqq"));
        Assert.Empty(FormatRegistry.GetSupportedTargetFormats(""));
    }

    /// <summary>
    /// Licence guard: RAR is proprietary and may only ever be read, never produced.
    /// If this test fails, do not "fix the test" — remove the target.
    /// </summary>
    [Fact]
    public void NoSourceOffersRarAsATarget()
    {
        var allSources = FormatRegistry.ImageFormats
            .Concat(FormatRegistry.VideoFormats)
            .Concat(FormatRegistry.AudioFormats)
            .Concat(FormatRegistry.DocumentFormats)
            .Concat(FormatRegistry.SpreadsheetFormats)
            .Concat(FormatRegistry.ArchiveFormats)
            .Concat(FormatRegistry.EBookFormats)
            .Concat(FormatRegistry.FontFormats)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var source in allSources)
        {
            Assert.DoesNotContain(".rar", FormatRegistry.GetSupportedTargetFormats(source),
                StringComparer.OrdinalIgnoreCase);
        }
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
        // Using b.bmp so that single[0] (".jpg") is a valid target for all files in the batch.
        var first = FormatRegistry.GetCommonTargetFormats(["a.png", "b.bmp"]);
        var second = FormatRegistry.GetCommonTargetFormats(["a.png", "b.bmp"]);

        Assert.Equal(first, second);
        Assert.Equal(single[0], first[0]);

        // When a file in the selection excludes single[0] via self-target exclusion,
        // verify the order of the remaining common targets still follows the first file's order.
        var withJpg = FormatRegistry.GetCommonTargetFormats(["a.png", "b.jpg"]);
        Assert.Equal(single.First(t => !t.Equals(".jpg", StringComparison.OrdinalIgnoreCase)), withJpg[0]);
    }

    [Fact]
    public void GetCommonTargetFormats_DisjointCategories_ReturnsEmpty()
    {
        // Images can go to .pdf, archives cannot, and archives can go to .7z, images cannot.
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
