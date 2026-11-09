using Xunit;

namespace QuickConvert.Engines.Media.Tests;

public class FfmpegProcessRunnerTests
{
    [Fact]
    public void PrepareArguments_AddsDefaultPrefix_ForFfmpeg()
    {
        var inputArgs = new[] { "-i", "input.mp4", "output.mp3" };
        var prepared = FfmpegProcessRunner.PrepareArguments("C:\\tools\\ffmpeg.exe", inputArgs);

        Assert.Equal(3 + 6, prepared.Count);
        Assert.Equal("-hide_banner", prepared[0]);
        Assert.Equal("-nostdin", prepared[1]);
        Assert.Equal("-y", prepared[2]);
        Assert.Equal("-nostats", prepared[3]);
        Assert.Equal("-loglevel", prepared[4]);
        Assert.Equal("error", prepared[5]);
        Assert.Equal("-i", prepared[6]);
        Assert.Equal("input.mp4", prepared[7]);
        Assert.Equal("output.mp3", prepared[8]);
    }

    [Fact]
    public void PrepareArguments_DoesNotDuplicatePrefix_IfAlreadyPresent()
    {
        var inputArgs = new[] { "-hide_banner", "-i", "input.mp4" };
        var prepared = FfmpegProcessRunner.PrepareArguments("ffmpeg.exe", inputArgs);

        Assert.Equal(inputArgs.Length, prepared.Count);
        Assert.Equal("-hide_banner", prepared[0]);
        Assert.Equal("-i", prepared[1]);
    }

    [Fact]
    public void PrepareArguments_DoesNotPrependPrefix_ForFfprobeOrOtherBinaries()
    {
        var probeArgs = new[] { "-v", "error", "-show_entries", "format=duration" };
        var prepared = FfmpegProcessRunner.PrepareArguments("ffprobe.exe", probeArgs);

        Assert.Equal(probeArgs.Length, prepared.Count);
        Assert.Equal(probeArgs, prepared);
    }

    [Fact]
    public void StderrBuffer_KeepsLast50Lines()
    {
        var runner = new FfmpegProcessRunner();

        for (int i = 0; i < 70; i++)
        {
            runner.RecordStderrLine($"Error line {i}");
        }

        var lines = runner.RecentStderr;
        Assert.Equal(50, lines.Count);
        Assert.Equal("Error line 20", lines[0]);
        Assert.Equal("Error line 69", lines[49]);

        var text = runner.GetRecentStderrText();
        Assert.StartsWith("Error line 20", text);
        Assert.EndsWith("Error line 69", text);
    }

    [Fact]
    public async Task RunAsync_ExecutesProcessAndCapturesOutput()
    {
        var runner = new FfmpegProcessRunner();
        string? capturedStdout = null;

        var exitCode = await runner.RunAsync(
            "dotnet.exe",
            new[] { "--version" },
            Environment.CurrentDirectory,
            onStdoutLine: line => capturedStdout = line,
            onStderrLine: null,
            ct: CancellationToken.None,
            idleTimeout: TimeSpan.FromSeconds(10));

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturedStdout);
        Assert.NotEmpty(capturedStdout);
    }

    [Fact]
    public async Task RunAsync_IdleTimeout_ThrowsTimeoutExceptionAndTerminates()
    {
        var runner = new FfmpegProcessRunner();

        var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await runner.RunAsync(
                "powershell.exe",
                new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 5" },
                Environment.CurrentDirectory,
                onStdoutLine: null,
                onStderrLine: null,
                ct: CancellationToken.None,
                idleTimeout: TimeSpan.FromMilliseconds(500));
        });

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var runner = new FfmpegProcessRunner();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await runner.RunAsync(
                "powershell.exe",
                new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 10" },
                Environment.CurrentDirectory,
                onStdoutLine: null,
                onStderrLine: null,
                ct: cts.Token,
                idleTimeout: TimeSpan.FromSeconds(30));
        });
    }
}
