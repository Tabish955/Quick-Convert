using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using Xunit;

namespace QuickConvert.Core.Tests;

public class ConversionEngineRouterTests
{
    [Fact]
    public void Resolve_EachEngineReceivesOnlyItsOwnFiles()
    {
        var router = new ConversionEngineRouter([new FakeConversionEngine(), new FakeDocsEngine()]);

        Assert.Equal("Fake Conversion Engine", router.EngineNameFor(".jpg", ".png"));
        Assert.Equal("Fake Docs", router.EngineNameFor(".txt", ".md"));
    }

    [Fact]
    public void Resolve_MatrixRefusesBeforeEnginesAreAsked()
    {
        // No engine claims archives, but the matrix refuses ZIP→PNG regardless.
        var router = new ConversionEngineRouter([new FakeConversionEngine()]);

        Assert.Null(router.Resolve(".zip", ".png"));
        Assert.Equal(string.Empty, router.EngineNameFor(".zip", ".png"));
    }

    [Fact]
    public void Resolve_CapabilityRefusesWhenNoEngineClaimsThePair()
    {
        var router = new ConversionEngineRouter([new FakeConversionEngine()]); // images only

        // The matrix allows .jpg → .pdf, but the fake engine cannot produce PDF.
        Assert.Null(router.Resolve(".jpg", ".pdf"));
    }

    [Fact]
    public void GetAvailableTargets_OnlyOffersWhatAnEngineCanProduce()
    {
        var router = new ConversionEngineRouter([new FakeConversionEngine()]); // images only

        var targets = router.GetAvailableTargets(".jpg");

        Assert.Contains(".png", targets);
        Assert.DoesNotContain(".rar", targets);          // licence guard, always
        Assert.Empty(router.GetAvailableTargets(".xyz")); // unknown source
    }

    [Fact]
    public void CanConvert_IsConsultedThroughResolve()
    {
        // A picky engine that claims nothing must starve the router even for matrix-valid pairs.
        var router = new ConversionEngineRouter([new NoOpEngine()]);

        Assert.Null(router.Resolve(".jpg", ".png"));
        Assert.Empty(router.GetAvailableTargets(".jpg"));
    }

    [Fact]
    public void Resolve_ReservedTargetExtract_RoutesDirectlyToEngine()
    {
        var router = new ConversionEngineRouter([new FakeArchiveEngine()]);
        var engine = router.Resolve(".zip", ConversionTargets.Extract);
        Assert.NotNull(engine);
        Assert.Equal("FakeArchive", engine.EngineName);
    }

    [Fact]
    public void Resolve_DirectorySource_RoutesDirectlyToEngine()
    {
        var router = new ConversionEngineRouter([new FakeArchiveEngine()]);
        var engine = router.Resolve("", ".zip");
        Assert.NotNull(engine);
        Assert.Equal("FakeArchive", engine.EngineName);

        // Directory to rar must be refused
        Assert.Null(router.Resolve("", ".rar"));
    }

    [Fact]
    public void GetAvailableTargets_ExtractableArchive_IncludesExtractTarget()
    {
        var router = new ConversionEngineRouter([new FakeArchiveEngine()]);
        var targets = router.GetAvailableTargets(".zip");
        Assert.Contains(ConversionTargets.Extract, targets);
    }

    [Fact]
    public void GetAvailableTargets_DirectorySource_ReturnsArchiveTargets()
    {
        var router = new ConversionEngineRouter([new FakeArchiveEngine()]);
        var targets = router.GetAvailableTargets("");
        Assert.Contains(".zip", targets);
        Assert.Contains(".7z", targets);
        Assert.DoesNotContain(".rar", targets);
    }

    private sealed class NoOpEngine : IConversionEngine
    {
        public string EngineName => "No-op";
        public IReadOnlyList<string> SupportedSourceExtensions => [];
        public bool CanConvert(string sourceExt, string targetExt) => false;
        public Task<ConversionResult> ConvertAsync(ConversionJob job, ConversionOptions options,
            IProgress<double>? progress, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeArchiveEngine : IConversionEngine
    {
        public string EngineName => "FakeArchive";
        public IReadOnlyList<string> SupportedSourceExtensions => [".zip", ".tar", ".gz", ".7z", ".rar"];
        public bool CanConvert(string sourceExt, string targetExt)
        {
            if (targetExt == ConversionTargets.Extract) return SupportedSourceExtensions.Contains(sourceExt);
            if (string.IsNullOrEmpty(sourceExt) || sourceExt == "<dir>") return targetExt is ".zip" or ".7z" or ".tar" or ".gz";
            return false;
        }
        public Task<ConversionResult> ConvertAsync(ConversionJob job, ConversionOptions options,
            IProgress<double>? progress, CancellationToken ct) =>
            Task.FromResult(new ConversionResult(true, job.InputFilePath + "_out"));
    }
}
