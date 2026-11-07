using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using Xunit;

namespace QuickConvert.Core.Tests;

public class BatchConversionCoordinatorTests
{
    private static BatchConversionCoordinator CreateSut(FakeConversionEngine? engine = null)
        => new(new ConversionEngineRouter([engine ?? new FakeConversionEngine()]));

    private static ConversionOptions Options(string target = ".jpg") =>
        new() { TargetExtension = target, Quality = 90 };

    [Fact]
    public void EnqueueFiles_SkipsPathsThatDoNotExist()
    {
        var sut = CreateSut(new FakeConversionEngine());

        var result = sut.EnqueueFiles(["C:\\nope\\missing.png", Path.Combine(Path.GetTempPath(), "also-missing.jpg")]);

        Assert.Empty(result.Added);
        Assert.Empty(result.Rejected);
        Assert.Empty(sut.Jobs);
    }

    [Fact]
    public void EnqueueFiles_InfersCategoryAndADefaultTarget()
    {
        using var file = new TempFile(".png");
        var sut = CreateSut(new FakeConversionEngine());

        var result = sut.EnqueueFiles([file.Path]);

        var job = Assert.Single(result.Added);
        Assert.Equal(FileCategory.Image, job.Category);
        Assert.False(string.IsNullOrWhiteSpace(job.TargetExtension));
        Assert.Single(sut.Jobs);
    }

    [Fact]
    public void EnqueueFiles_DoesNotQueueTheSameFileTwice()
    {
        using var file = new TempFile(".png");
        var sut = CreateSut(new FakeConversionEngine());

        sut.EnqueueFiles([file.Path]);
        sut.EnqueueFiles([file.Path]);

        Assert.Single(sut.Jobs);
    }

    [Fact]
    public void EnqueueFiles_RefusesImpossibleConversion_BeforeItReachesTheQueue()
    {
        using var zip = new TempFile(".zip");
        var sut = CreateSut(new FakeConversionEngine()); // images only, even if it claimed archives

        var result = sut.EnqueueFiles([zip.Path], ".png");

        Assert.Empty(result.Added);
        var rejected = Assert.Single(result.Rejected);
        Assert.Contains(".png", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Jobs); // refused BEFORE the queue, per spec §3
    }

    [Fact]
    public async Task StartAllAsync_CompletesJobsAndReportsProgress()
    {
        using var file = new TempFile(".png");
        var sut = CreateSut(new FakeConversionEngine());
        sut.EnqueueFiles([file.Path]);

        await sut.StartAllAsync(Options(".jpg"));

        var job = Assert.Single(sut.Jobs);
        Assert.Equal(ConversionStatus.Completed, job.Status);
        Assert.False(string.IsNullOrWhiteSpace(job.OutputFilePath));
        Assert.Equal(1, sut.CompletedJobsCount);
        Assert.Equal(100, sut.OverallProgress);
        Assert.False(sut.IsConverting);
    }

    [Fact]
    public async Task StartAllAsync_EngineFailure_MarksJobFailedWithTheReason()
    {
        using var file = new TempFile(".png");
        var sut = CreateSut(new FakeConversionEngine { FailConversions = true });
        sut.EnqueueFiles([file.Path]);

        await sut.StartAllAsync(Options());

        var job = Assert.Single(sut.Jobs);
        Assert.Equal(ConversionStatus.Failed, job.Status);
        Assert.Equal("fake failure", job.ErrorMessage);
        Assert.Equal(0, sut.CompletedJobsCount);
    }

    [Fact]
    public async Task StartAllAsync_WithNoJobs_IsANoOp()
    {
        var sut = CreateSut(new FakeConversionEngine());

        await sut.StartAllAsync(Options());

        Assert.False(sut.IsConverting);
        Assert.Empty(sut.Jobs);
    }

    [Fact]
    public void CancelJob_CancelsAJobThatHasNotStartedYet()
    {
        using var file = new TempFile(".png");
        var sut = CreateSut(new FakeConversionEngine());
        var job = Assert.Single(sut.EnqueueFiles([file.Path]).Added);

        sut.CancelJob(job.Id);

        Assert.Equal(ConversionStatus.Cancelled, job.Status);
        Assert.Equal("Cancelled", job.StatusText);
    }

    [Fact]
    public void CancelAll_CancelsEveryPendingJob()
    {
        using var a = new TempFile(".png");
        using var b = new TempFile(".jpg");
        var sut = CreateSut(new FakeConversionEngine());
        sut.EnqueueFiles([a.Path, b.Path]);
        Assert.Equal(2, sut.Jobs.Count);

        sut.CancelAll();

        Assert.All(sut.Jobs, j => Assert.Equal(ConversionStatus.Cancelled, j.Status));
    }

    [Fact]
    public void CancelledFiles_CanBeRequeued()
    {
        using var file = new TempFile(".png");
        var sut = CreateSut(new FakeConversionEngine());
        sut.EnqueueFiles([file.Path]);
        sut.CancelAll();

        var result = sut.EnqueueFiles([file.Path]);

        Assert.Single(result.Added);
        Assert.Equal(2, sut.Jobs.Count);
    }

    [Fact]
    public void ClearCompleted_RemovesOnlyFinishedJobs()
    {
        using var a = new TempFile(".png");
        using var b = new TempFile(".png");
        var sut = CreateSut(new FakeConversionEngine());
        var jobs = sut.EnqueueFiles([a.Path, b.Path]).Added;

        sut.CancelJob(jobs[0].Id);
        sut.ClearCompleted();

        var remaining = Assert.Single(sut.Jobs);
        Assert.Equal(ConversionStatus.Pending, remaining.Status);
    }

    [Fact]
    public void EnqueueFiles_FiresJobRejected_ForUnsupportedSource()
    {
        using var file = new TempFile(".xyz");
        var sut = CreateSut(new FakeConversionEngine());

        RejectedFile? seen = null;
        sut.JobRejected += (s, r) => seen = r;

        var result = sut.EnqueueFiles([file.Path]);

        Assert.Empty(result.Added);
        Assert.NotNull(seen);
        Assert.Equal(file.Path, seen!.FilePath);
    }
}
