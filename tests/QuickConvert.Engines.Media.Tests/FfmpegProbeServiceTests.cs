using Xunit;

namespace QuickConvert.Engines.Media.Tests;

public class FfmpegProbeServiceTests
{
    [Fact]
    public async Task GetCapabilitiesAsync_ReturnsUnavailable_WhenBinariesNotFound()
    {
        var locator = new FfmpegLocator("C:\\nonexistent\\ffmpeg.exe", "C:\\nonexistent\\ffprobe.exe");
        var fakeRunner = new FakeFfmpegProcessRunner();
        var probeService = new FfmpegProbeService(fakeRunner, locator);

        var caps = await probeService.GetCapabilitiesAsync();

        Assert.False(caps.IsAvailable);
        Assert.Null(caps.Version);
        Assert.Empty(caps.AllEncoders);
        Assert.Empty(caps.HardwareEncoders);
        Assert.Empty(fakeRunner.Invocations);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_ParsesVersionAndEncoders_AndValidatesHardware()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "QuickConvert_ProbeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeFfmpeg = Path.Combine(tempDir, "ffmpeg.exe");
            var fakeFfprobe = Path.Combine(tempDir, "ffprobe.exe");
            File.WriteAllText(fakeFfmpeg, "binary");
            File.WriteAllText(fakeFfprobe, "binary");

            var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);
            var fakeRunner = new FakeFfmpegProcessRunner();

            fakeRunner.OutputEmitter = (stdout, stderr) =>
            {
                var lastInvocation = fakeRunner.Invocations[^1];
                if (lastInvocation.Arguments.Contains("-version"))
                {
                    stdout?.Invoke("ffmpeg version 9.0.2-essentials_build-www.gyan.dev Copyright (c) 2000-2026 the FFmpeg developers");
                    stdout?.Invoke("built with gcc 14.2.0");
                }
                else if (lastInvocation.Arguments.Contains("-encoders"))
                {
                    stdout?.Invoke("Encoders:");
                    stdout?.Invoke(" V..... = Video");
                    stdout?.Invoke(" A..... = Audio");
                    stdout?.Invoke(" ------");
                    stdout?.Invoke(" V..... libx264              libx264 H.264 / AVC / MPEG-4 AVC");
                    stdout?.Invoke(" V..... h264_nvenc           NVIDIA NVENC H.264 encoder");
                    stdout?.Invoke(" V..... h264_qsv             Intel Quick Sync Video H.264");
                    stdout?.Invoke(" A..... aac                  AAC (Advanced Audio Coding)");
                    stdout?.Invoke(" A..... libmp3lame           libmp3lame MP3");
                }
            };

            // Exit code selector: succeed on -version, -encoders, and for h264_nvenc trial test,
            // but fail for h264_qsv trial test.
            fakeRunner.ExitCodeSelector = (exe, args) =>
            {
                if (args.Contains("-c:v") && args.Contains("h264_qsv"))
                {
                    return 1; // Simulation: QSV driver listed, but GPU unsupported
                }
                return 0; // nvenc and other calls succeed
            };

            var probeService = new FfmpegProbeService(fakeRunner, locator);

            var caps = await probeService.GetCapabilitiesAsync();

            Assert.True(caps.IsAvailable);
            Assert.Equal("9.0.2-essentials_build-www.gyan.dev", caps.Version);
            Assert.True(caps.HasEncoder("libx264"));
            Assert.True(caps.HasEncoder("aac"));
            Assert.True(caps.HasEncoder("libmp3lame"));

            // nvenc passed trial probe; qsv failed
            Assert.True(caps.HasHardwareEncoder("h264_nvenc"));
            Assert.False(caps.HasHardwareEncoder("h264_qsv"));
            Assert.Equal("h264_nvenc", caps.PreferredH264Encoder);

            // Test caching: second call returns exact same instance without running extra processes
            int invocationCount = fakeRunner.Invocations.Count;
            var cachedCaps = probeService.GetCapabilities();
            Assert.Same(caps, cachedCaps);
            Assert.Equal(invocationCount, fakeRunner.Invocations.Count);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetCapabilitiesAsync_FallsBackToLibx264_WhenNoHardwareEncoderWorks()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "QuickConvert_ProbeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeFfmpeg = Path.Combine(tempDir, "ffmpeg.exe");
            var fakeFfprobe = Path.Combine(tempDir, "ffprobe.exe");
            File.WriteAllText(fakeFfmpeg, "binary");
            File.WriteAllText(fakeFfprobe, "binary");

            var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);
            var fakeRunner = new FakeFfmpegProcessRunner();

            fakeRunner.OutputEmitter = (stdout, stderr) =>
            {
                var lastInvocation = fakeRunner.Invocations[^1];
                if (lastInvocation.Arguments.Contains("-encoders"))
                {
                    stdout?.Invoke(" ------");
                    stdout?.Invoke(" V..... libx264              libx264 H.264");
                    stdout?.Invoke(" V..... h264_amf             AMD AMF H.264");
                }
            };

            // amf fails trial test
            fakeRunner.ExitCodeSelector = (exe, args) =>
            {
                if (args.Contains("-c:v") && args.Contains("h264_amf"))
                {
                    return -1;
                }
                return 0;
            };

            var probeService = new FfmpegProbeService(fakeRunner, locator);
            var caps = await probeService.GetCapabilitiesAsync();

            Assert.True(caps.IsAvailable);
            Assert.Empty(caps.HardwareEncoders);
            Assert.Equal("libx264", caps.PreferredH264Encoder);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
