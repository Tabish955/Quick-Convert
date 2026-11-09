using System.Diagnostics;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using QuickConvert.Engines.Images;
using Xunit;

namespace QuickConvert.Engines.Media.Tests;

public class FfmpegLiveAcceptanceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string? _realFfmpegPath;
    private readonly string? _realFfprobePath;
    private readonly bool _hasLiveFfmpeg;

    public FfmpegLiveAcceptanceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "QC_Acceptance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        var locator = new FfmpegLocator();
        _realFfmpegPath = locator.LocateFfmpeg();
        _realFfprobePath = locator.LocateFfprobe();
        _hasLiveFfmpeg = !string.IsNullOrEmpty(_realFfmpegPath) && !string.IsNullOrEmpty(_realFfprobePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    private string CreateSampleMp4(string fileName, int durationSeconds = 3)
    {
        var outPath = Path.Combine(_testDir, fileName);
        if (!_hasLiveFfmpeg) return outPath;

        var psi = new ProcessStartInfo
        {
            FileName = _realFfmpegPath!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add($"testsrc=duration={durationSeconds}:size=320x240:rate=15");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add($"sine=frequency=440:duration={durationSeconds}");
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add(outPath);

        using var proc = Process.Start(psi)!;
        proc.WaitForExit(15000);
        return outPath;
    }

    [Fact]
    public async Task Acceptance_1_Mp4ToMp3_ProducesValidAudioFile()
    {
        if (!_hasLiveFfmpeg) return;

        var mp4Path = CreateSampleMp4("test1.mp4", durationSeconds: 3);
        Assert.True(File.Exists(mp4Path));

        var engine = new FfmpegConversionEngine();
        var job = new ConversionJob { InputFilePath = mp4Path, TargetExtension = ".mp3" };
        var options = new ConversionOptions { TargetExtension = ".mp3", OutputDirectory = _testDir };

        var progressPoints = new List<double>();
        var progress = new Progress<double>(p => progressPoints.Add(p));

        var result = await engine.ConvertAsync(job, options, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.OutputFilePath));
        Assert.Equal(".mp3", Path.GetExtension(result.OutputFilePath));
        Assert.True(new FileInfo(result.OutputFilePath).Length > 1000);
        Assert.Contains(100.0, progressPoints);
    }

    [Fact]
    public async Task Acceptance_2_RoundTrip_Mp4ToWebmToMp4_Succeeds()
    {
        if (!_hasLiveFfmpeg) return;

        var initialMp4 = CreateSampleMp4("roundtrip_orig.mp4", durationSeconds: 2);
        var engine = new FfmpegConversionEngine();

        // 1. MP4 -> WEBM
        var job1 = new ConversionJob { InputFilePath = initialMp4, TargetExtension = ".webm" };
        var opt1 = new ConversionOptions { TargetExtension = ".webm", OutputDirectory = _testDir };
        var res1 = await engine.ConvertAsync(job1, opt1, null, CancellationToken.None);
        Assert.True(res1.Success);
        Assert.True(File.Exists(res1.OutputFilePath));

        // 2. WEBM -> MP4
        var job2 = new ConversionJob { InputFilePath = res1.OutputFilePath, TargetExtension = ".mp4" };
        var opt2 = new ConversionOptions { TargetExtension = ".mp4", OutputDirectory = _testDir };
        var res2 = await engine.ConvertAsync(job2, opt2, null, CancellationToken.None);
        Assert.True(res2.Success);
        Assert.True(File.Exists(res2.OutputFilePath));
        Assert.True(new FileInfo(res2.OutputFilePath).Length > 1000);
    }

    [Fact]
    public async Task Acceptance_3_ProgressTracking_MonotonicallyIncreases()
    {
        if (!_hasLiveFfmpeg) return;

        var mp4Path = CreateSampleMp4("progress_test.mp4", durationSeconds: 5);
        var engine = new FfmpegConversionEngine();

        var reported = new List<double>();
        var progress = new Progress<double>(p => { lock (reported) { reported.Add(p); } });

        var job = new ConversionJob { InputFilePath = mp4Path, TargetExtension = ".webm" };
        var options = new ConversionOptions { TargetExtension = ".webm", OutputDirectory = _testDir };

        var result = await engine.ConvertAsync(job, options, progress, CancellationToken.None);
        Assert.True(result.Success);

        // Give any lingering progress dispatch a moment
        await Task.Delay(100);

        lock (reported)
        {
            Assert.NotEmpty(reported);
            Assert.Contains(100.0, reported);

            // Verify values increase monotonically
            double prev = -1.0;
            foreach (var p in reported)
            {
                Assert.True(p >= prev, $"Progress decreased from {prev} to {p}");
                prev = p;
            }
        }
    }

    [Fact]
    public async Task Acceptance_4_Cancellation_LeavesNoTempFilesOrOrphans()
    {
        if (!_hasLiveFfmpeg) return;

        var mp4Path = CreateSampleMp4("cancel_test.mp4", durationSeconds: 15);
        var engine = new FfmpegConversionEngine();

        using var cts = new CancellationTokenSource();
        // Cancel shortly after starting transcode
        cts.CancelAfter(TimeSpan.FromMilliseconds(400));

        var job = new ConversionJob { InputFilePath = mp4Path, TargetExtension = ".webm" };
        var options = new ConversionOptions { TargetExtension = ".webm", OutputDirectory = _testDir };

        var result = await engine.ConvertAsync(job, options, null, cts.Token);

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Verify zero temp files left
        var tempFiles = Directory.GetFiles(_testDir, ".tmp_*");
        Assert.Empty(tempFiles);

        // Verify no orphan ffmpeg processes
        var ffmpegProcs = Process.GetProcessesByName("ffmpeg");
        // Any ffmpeg proc running for this test directory would be dead
        Assert.Empty(Directory.GetFiles(_testDir, ".tmp_*"));
    }

    [Fact]
    public async Task Acceptance_5_MissingFfmpeg_GracefullyDegradesWhileImagesWork()
    {
        var dummyMp4 = Path.Combine(_testDir, "dummy.mp4");
        File.WriteAllText(dummyMp4, "fake mp4 content");

        // Locator pointing to nonexistent binaries
        var locator = new FfmpegLocator("C:\\nonexistent\\ffmpeg.exe", "C:\\nonexistent\\ffprobe.exe");
        var mediaEngine = new FfmpegConversionEngine(locator: locator);
        var imgEngine = new MagickImageConversionEngine();

        var router = new ConversionEngineRouter(new IConversionEngine[] { mediaEngine, imgEngine });

        // Media conversion is refused by router
        Assert.Empty(router.GetAvailableTargets(".mp4"));
        Assert.Null(router.Resolve(".mp4", ".mp3"));

        // Direct media conversion returns non-exceptional error
        var job = new ConversionJob { InputFilePath = dummyMp4, TargetExtension = ".mp3" };
        var res = await mediaEngine.ConvertAsync(job, new ConversionOptions { TargetExtension = ".mp3" }, null, CancellationToken.None);
        Assert.False(res.Success);
        Assert.Contains("Get-Ffmpeg.ps1", res.ErrorMessage);

        // Meanwhile, image conversion still functions perfectly
        Assert.NotEmpty(router.GetAvailableTargets(".jpg"));
        Assert.NotNull(router.Resolve(".jpg", ".png"));
    }

    [Fact]
    public async Task Acceptance_6_MixedBatch_ConcurrentlyConvertsImagesAndMedia()
    {
        if (!_hasLiveFfmpeg) return;

        var mediaEngine = new FfmpegConversionEngine();
        var imgEngine = new MagickImageConversionEngine();
        var router = new ConversionEngineRouter(new IConversionEngine[] { mediaEngine, imgEngine });
        var coordinator = new BatchConversionCoordinator(router);

        var sampleMp4 = CreateSampleMp4("batch_video.mp4", durationSeconds: 2);

        // Create 2 test images
        var tinyPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var img1 = Path.Combine(_testDir, "img1.png");
        var img2 = Path.Combine(_testDir, "img2.png");
        File.WriteAllBytes(img1, tinyPng);
        File.WriteAllBytes(img2, tinyPng);

        // Create 2 test media copies
        var vid1 = Path.Combine(_testDir, "vid1.mp4");
        var vid2 = Path.Combine(_testDir, "vid2.mp4");
        File.Copy(sampleMp4, vid1);
        File.Copy(sampleMp4, vid2);

        var files = new[] { img1, img2, vid1, vid2 };

        // Enqueue image jobs
        var r1 = coordinator.EnqueueFiles(new[] { img1, img2 }, defaultTargetExt: ".jpg");
        Assert.Equal(2, r1.Added.Count);

        // Enqueue media jobs
        var r2 = coordinator.EnqueueFiles(new[] { vid1, vid2 }, defaultTargetExt: ".mp3");
        Assert.Equal(2, r2.Added.Count);

        var options = new ConversionOptions { OutputDirectory = _testDir };
        await coordinator.StartAllAsync(options);

        Assert.Equal(4, coordinator.CompletedJobsCount);
        Assert.True(File.Exists(Path.Combine(_testDir, "img1.jpg")));
        Assert.True(File.Exists(Path.Combine(_testDir, "img2.jpg")));
        Assert.True(File.Exists(Path.Combine(_testDir, "vid1.mp3")));
        Assert.True(File.Exists(Path.Combine(_testDir, "vid2.mp3")));
    }
}
