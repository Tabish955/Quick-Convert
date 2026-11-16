using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using QuickConvert.Core.Tools;
using Xunit;

namespace QuickConvert.Core.Tests;

public class ToolProbeServiceTests
{
    [Fact]
    public void ToolProbe_WhenAllToolsAvailable_ReportsAllPresent()
    {
        var probe = new ToolProbeService(
            isFfmpegAvailable: () => true,
            isPandocAvailable: () => true,
            isWkhtmltopdfAvailable: () => true,
            isSevenZipAvailable: () => true);

        var result = probe.Probe();

        Assert.True(result.AllPresent);
        Assert.Empty(result.MissingTools);
    }

    [Fact]
    public void ToolProbe_WhenFfmpegMissing_ReportsFfmpegAndScript()
    {
        var probe = new ToolProbeService(
            isFfmpegAvailable: () => false,
            isPandocAvailable: () => true,
            isWkhtmltopdfAvailable: () => true,
            isSevenZipAvailable: () => true);

        var result = probe.Probe();

        Assert.False(result.AllPresent);
        Assert.Single(result.MissingTools);
        var missing = result.MissingTools[0];
        Assert.Equal("FFmpeg", missing.ToolName);
        Assert.Equal("ffmpeg.exe", missing.ExeName);
        Assert.Contains("Get-Ffmpeg.ps1", missing.SetupScript);
        Assert.Contains("Audio and video", missing.AffectedConversions);
    }

    [Fact]
    public void ToolProbe_WhenPandocAndWkhtmltopdfMissing_ReportsBoth()
    {
        var probe = new ToolProbeService(
            isFfmpegAvailable: () => true,
            isPandocAvailable: () => false,
            isWkhtmltopdfAvailable: () => false,
            isSevenZipAvailable: () => true);

        var result = probe.Probe();

        Assert.False(result.AllPresent);
        Assert.Equal(2, result.MissingTools.Count);
        Assert.Contains(result.MissingTools, t => t.ToolName == "Pandoc" && t.SetupScript.Contains("Get-Pandoc.ps1"));
        Assert.Contains(result.MissingTools, t => t.ToolName == "wkhtmltopdf" && t.SetupScript.Contains("Get-Wkhtmltopdf.ps1"));
    }

    [Fact]
    public void ToolProbe_WhenSevenZipMissing_ReportsSevenZip()
    {
        var probe = new ToolProbeService(
            isFfmpegAvailable: () => true,
            isPandocAvailable: () => true,
            isWkhtmltopdfAvailable: () => true,
            isSevenZipAvailable: () => false);

        var result = probe.Probe();

        Assert.False(result.AllPresent);
        Assert.Single(result.MissingTools);
        var missing = result.MissingTools[0];
        Assert.Equal("7-Zip", missing.ToolName);
        Assert.Equal("7za.exe", missing.ExeName);
        Assert.Contains("Get-7za.ps1", missing.SetupScript);
        Assert.Contains(".7z", missing.AffectedConversions);
    }

    [Fact]
    public async Task ToolProbe_ProbeAsync_ReturnsMatchingResult()
    {
        var probe = new ToolProbeService(
            isFfmpegAvailable: () => false,
            isPandocAvailable: () => false,
            isWkhtmltopdfAvailable: () => true,
            isSevenZipAvailable: () => false);

        var result = await probe.ProbeAsync();

        Assert.False(result.AllPresent);
        Assert.Equal(3, result.MissingTools.Count);
    }

    [Fact]
    public void EngineAvailability_WhenMediaEngineUnavailable_ImageTargetsRemainAvailable()
    {
        // Media engine disabled (simulating missing ffmpeg)
        var disabledMediaEngine = new MockConditionalEngine("FFmpeg", isAvailable: false, sources: [".mp4", ".mp3"]);
        // Image engine always available without external tools
        var imageEngine = new FakeConversionEngine();

        var router = new ConversionEngineRouter([disabledMediaEngine, imageEngine]);

        // .mp4 has no available targets because media engine is disabled
        var mediaTargets = router.GetAvailableTargets(".mp4");
        Assert.Empty(mediaTargets);

        // .png still has valid image targets
        var imageTargets = router.GetAvailableTargets(".png");
        Assert.NotEmpty(imageTargets);
        Assert.Contains(".jpg", imageTargets);
        Assert.Contains(".webp", imageTargets);
    }

    [Fact]
    public void EngineAvailability_WhenArchiveEngineLacks7za_StandardFormatsRemainAvailable()
    {
        // Engine that supports zip/tar but cannot compress to 7z because 7za is missing
        var partialArchiveEngine = new MockArchiveEngine(canCreate7z: false);
        var router = new ConversionEngineRouter([partialArchiveEngine]);

        var folderTargets = router.GetAvailableTargets("<dir>");
        Assert.Contains(".zip", folderTargets);
        Assert.Contains(".tar", folderTargets);
        Assert.DoesNotContain(".7z", folderTargets);
    }

    private sealed class MockConditionalEngine : IConversionEngine
    {
        private readonly bool _isAvailable;
        public string EngineName { get; }
        public IReadOnlyList<string> SupportedSourceExtensions { get; }

        public MockConditionalEngine(string name, bool isAvailable, string[] sources)
        {
            EngineName = name;
            _isAvailable = isAvailable;
            SupportedSourceExtensions = sources;
        }

        public bool CanConvert(string sourceExt, string targetExt) => _isAvailable;

        public Task<ConversionResult> ConvertAsync(ConversionJob job, ConversionOptions options, IProgress<double>? progress, CancellationToken ct) =>
            Task.FromResult(new ConversionResult(_isAvailable, "out.tmp"));
    }

    private sealed class MockArchiveEngine : IConversionEngine
    {
        private readonly bool _canCreate7z;
        public string EngineName => "Archive";
        public IReadOnlyList<string> SupportedSourceExtensions => [".zip", ".tar", ".7z"];

        public MockArchiveEngine(bool canCreate7z) => _canCreate7z = canCreate7z;

        public bool CanConvert(string sourceExt, string targetExt)
        {
            if (string.IsNullOrEmpty(sourceExt) || sourceExt == "<dir>")
            {
                if (targetExt == ".7z") return _canCreate7z;
                return targetExt is ".zip" or ".tar" or ".gz";
            }
            return false;
        }

        public Task<ConversionResult> ConvertAsync(ConversionJob job, ConversionOptions options, IProgress<double>? progress, CancellationToken ct) =>
            Task.FromResult(new ConversionResult(true, "out.tmp"));
    }
}
