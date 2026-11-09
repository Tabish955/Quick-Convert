using QuickConvert.Core.Models;
using Xunit;

namespace QuickConvert.Engines.Media.Tests;

public class FfmpegConversionEngineTests : IDisposable
{
    private readonly string _testDir;

    public FfmpegConversionEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "QuickConvert_EngineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
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

    [Fact]
    public void CanConvert_ReturnsFalse_WhenBinaryUnavailable()
    {
        var locator = new FfmpegLocator("C:\\nonexistent\\ffmpeg.exe", "C:\\nonexistent\\ffprobe.exe");
        var engine = new FfmpegConversionEngine(locator: locator);

        Assert.False(engine.CanConvert(".mp4", ".mp3"));
    }

    [Fact]
    public void CanConvert_ReturnsFalse_ForNonMediaPairs()
    {
        var fakeFfmpeg = Path.Combine(_testDir, "ffmpeg.exe");
        var fakeFfprobe = Path.Combine(_testDir, "ffprobe.exe");
        File.WriteAllText(fakeFfmpeg, "dummy");
        File.WriteAllText(fakeFfprobe, "dummy");

        var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);
        var engine = new FfmpegConversionEngine(locator: locator);

        Assert.False(engine.CanConvert(".png", ".jpg"));
        Assert.False(engine.CanConvert(".zip", ".7z"));
        Assert.False(engine.CanConvert(".mp4", ".rar"));
    }

    [Fact]
    public void CanConvert_ReturnsTrue_ForMatrixAllowedMediaPairs()
    {
        var fakeFfmpeg = Path.Combine(_testDir, "ffmpeg.exe");
        var fakeFfprobe = Path.Combine(_testDir, "ffprobe.exe");
        File.WriteAllText(fakeFfmpeg, "dummy");
        File.WriteAllText(fakeFfprobe, "dummy");

        var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);
        var engine = new FfmpegConversionEngine(locator: locator);

        Assert.True(engine.CanConvert(".mp4", ".mp3"));
        Assert.True(engine.CanConvert(".mp4", ".webm"));
        Assert.True(engine.CanConvert(".wav", ".flac"));
        Assert.True(engine.CanConvert(".mov", ".gif"));
    }

    [Fact]
    public async Task ConvertAsync_MissingInputFile_ReturnsFailedResult()
    {
        var engine = new FfmpegConversionEngine();
        var job = new ConversionJob { InputFilePath = "C:\\nonexistent\\video.mp4", TargetExtension = ".mp3" };
        var options = new ConversionOptions { TargetExtension = ".mp3" };

        var result = await engine.ConvertAsync(job, options, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("File not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ConvertAsync_MissingFfmpeg_ReturnsClearHelpMessage()
    {
        var inputFile = Path.Combine(_testDir, "sample.mp4");
        File.WriteAllText(inputFile, "sample video data");

        var locator = new FfmpegLocator("C:\\nonexistent\\ffmpeg.exe", "C:\\nonexistent\\ffprobe.exe");
        var engine = new FfmpegConversionEngine(locator: locator);

        var job = new ConversionJob { InputFilePath = inputFile, TargetExtension = ".mp3" };
        var options = new ConversionOptions { TargetExtension = ".mp3" };

        var result = await engine.ConvertAsync(job, options, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Get-Ffmpeg.ps1", result.ErrorMessage);
    }

    [Fact]
    public async Task ConvertAsync_HappyPath_PromotesOutputAndReportsProgress()
    {
        var inputFile = Path.Combine(_testDir, "video.mp4");
        File.WriteAllText(inputFile, "dummy media bytes");

        var fakeFfmpeg = Path.Combine(_testDir, "ffmpeg.exe");
        var fakeFfprobe = Path.Combine(_testDir, "ffprobe.exe");
        File.WriteAllText(fakeFfmpeg, "dummy");
        File.WriteAllText(fakeFfprobe, "dummy");

        var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);
        var fakeRunner = new FakeFfmpegProcessRunner();

        // Simulate ffprobe duration probe and ffmpeg transcode output
        fakeRunner.OutputEmitter = (stdout, stderr) =>
        {
            var invocation = fakeRunner.Invocations[^1];
            if (invocation.ExecutablePath.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
            {
                stdout?.Invoke("10.0"); // 10 seconds duration
            }
            else
            {
                // Emit progress lines
                stdout?.Invoke("out_time_us=5000000"); // 50%
                stdout?.Invoke("progress=end");        // 95%
                // Simulate output file creation by ffmpeg
                var outPath = invocation.Arguments[^1];
                File.WriteAllText(outPath, "transcoded output data");
            }
        };

        var engine = new FfmpegConversionEngine(fakeRunner, locator);

        var reportedProgress = new List<double>();
        var progress = new Progress<double>(reportedProgress.Add);

        var job = new ConversionJob { InputFilePath = inputFile, TargetExtension = ".mp3" };
        var options = new ConversionOptions { TargetExtension = ".mp3", OutputDirectory = _testDir };

        var result = await engine.ConvertAsync(job, options, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.OutputFilePath));
        Assert.Equal(".mp3", Path.GetExtension(result.OutputFilePath));

        // Ensure no temporary .tmp_* files remain
        var remainingTemp = Directory.GetFiles(_testDir, ".tmp_*");
        Assert.Empty(remainingTemp);

        // Progress reached 100%
        Assert.Contains(100.0, reportedProgress);
    }

    [Fact]
    public async Task ConvertAsync_HardwareEncoderFails_FallsBackToSoftwareEncoder()
    {
        var inputFile = Path.Combine(_testDir, "clip.webm");
        File.WriteAllText(inputFile, "dummy webm");

        var fakeFfmpeg = Path.Combine(_testDir, "ffmpeg.exe");
        var fakeFfprobe = Path.Combine(_testDir, "ffprobe.exe");
        File.WriteAllText(fakeFfmpeg, "dummy");
        File.WriteAllText(fakeFfprobe, "dummy");

        var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);
        var fakeRunner = new FakeFfmpegProcessRunner();

        // Capabilities: nvenc is reported as preferred
        var caps = new FfmpegCapabilities(
            isAvailable: true,
            version: "9.0.2",
            ffmpegPath: fakeFfmpeg,
            ffprobePath: fakeFfprobe,
            allEncoders: new[] { "libx264", "h264_nvenc" },
            hardwareEncoders: new[] { "h264_nvenc" });

        var fakeProbeService = new FakeProbeService(caps);

        int transcodeAttempts = 0;
        fakeRunner.ExitCodeSelector = (exe, args) =>
        {
            if (exe.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase)) return 0;

            transcodeAttempts++;
            if (args.Contains("h264_nvenc"))
            {
                return 1; // nvenc transcode failed at runtime
            }
            if (args.Contains("libx264"))
            {
                // Fallback software transcode succeeds
                var outPath = args[^1];
                File.WriteAllText(outPath, "software transcoded mp4");
                return 0;
            }
            return 0;
        };

        var engine = new FfmpegConversionEngine(fakeRunner, locator, fakeProbeService);

        var job = new ConversionJob { InputFilePath = inputFile, TargetExtension = ".mp4" };
        var options = new ConversionOptions { TargetExtension = ".mp4", OutputDirectory = _testDir };

        var result = await engine.ConvertAsync(job, options, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, transcodeAttempts); // Attempted hw first, then fell back to software
        Assert.True(File.Exists(result.OutputFilePath));
    }

    [Fact]
    public async Task ConvertAsync_Cancellation_CleansUpTempFileAndPreservesExistingDestination()
    {
        var inputFile = Path.Combine(_testDir, "video.mp4");
        File.WriteAllText(inputFile, "source video");

        // Destination file already exists
        var existingDestination = Path.Combine(_testDir, "video.mp3");
        File.WriteAllText(existingDestination, "ORIGINAL UNTOUCHED CONTENT");

        var fakeFfmpeg = Path.Combine(_testDir, "ffmpeg.exe");
        var fakeFfprobe = Path.Combine(_testDir, "ffprobe.exe");
        File.WriteAllText(fakeFfmpeg, "dummy");
        File.WriteAllText(fakeFfprobe, "dummy");

        var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);
        var fakeRunner = new FakeFfmpegProcessRunner
        {
            Delay = TimeSpan.FromMilliseconds(500)
        };

        var engine = new FfmpegConversionEngine(fakeRunner, locator);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var job = new ConversionJob { InputFilePath = inputFile, TargetExtension = ".mp3" };
        var options = new ConversionOptions { TargetExtension = ".mp3", OutputDirectory = _testDir, OverwriteExisting = true };

        var result = await engine.ConvertAsync(job, options, null, cts.Token);

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Original destination must remain unchanged!
        Assert.True(File.Exists(existingDestination));
        Assert.Equal("ORIGINAL UNTOUCHED CONTENT", File.ReadAllText(existingDestination));

        // No leftover temp files
        Assert.Empty(Directory.GetFiles(_testDir, ".tmp_*"));
    }

    [Fact]
    public async Task ConvertAsync_ProcessError_ReturnsErrorMessageAndCleansUp()
    {
        var inputFile = Path.Combine(_testDir, "bad.mp4");
        File.WriteAllText(inputFile, "corrupt bytes");

        var fakeFfmpeg = Path.Combine(_testDir, "ffmpeg.exe");
        var fakeFfprobe = Path.Combine(_testDir, "ffprobe.exe");
        File.WriteAllText(fakeFfmpeg, "dummy");
        File.WriteAllText(fakeFfprobe, "dummy");

        var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);
        var fakeRunner = new FakeFfmpegProcessRunner();

        fakeRunner.ExitCodeSelector = (exe, args) =>
        {
            if (exe.EndsWith("ffprobe.exe", StringComparison.OrdinalIgnoreCase)) return 0;
            return 1; // Error
        };

        fakeRunner.OutputEmitter = (_, stderr) =>
        {
            stderr?.Report("Invalid data found when processing input");
        };

        var engine = new FfmpegConversionEngine(fakeRunner, locator);

        var job = new ConversionJob { InputFilePath = inputFile, TargetExtension = ".mp3" };
        var options = new ConversionOptions { TargetExtension = ".mp3", OutputDirectory = _testDir };

        var result = await engine.ConvertAsync(job, options, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("exit code 1", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_testDir, ".tmp_*"));
    }

    private sealed class FakeProbeService : IFfmpegProbeService
    {
        private readonly FfmpegCapabilities _caps;
        public FakeProbeService(FfmpegCapabilities caps) => _caps = caps;
        public FfmpegCapabilities GetCapabilities() => _caps;
        public Task<FfmpegCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) => Task.FromResult(_caps);
    }
}
