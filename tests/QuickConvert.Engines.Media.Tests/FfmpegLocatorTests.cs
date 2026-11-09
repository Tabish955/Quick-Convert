using Xunit;

namespace QuickConvert.Engines.Media.Tests;

public class FfmpegLocatorTests
{
    [Fact]
    public void Overrides_TakePrecedence_WhenFilesExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "QuickConvert_LocatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeFfmpeg = Path.Combine(tempDir, "ffmpeg.exe");
            var fakeFfprobe = Path.Combine(tempDir, "ffprobe.exe");
            File.WriteAllText(fakeFfmpeg, "dummy ffmpeg");
            File.WriteAllText(fakeFfprobe, "dummy ffprobe");

            var locator = new FfmpegLocator(fakeFfmpeg, fakeFfprobe);

            Assert.Equal(fakeFfmpeg, locator.LocateFfmpeg());
            Assert.Equal(fakeFfprobe, locator.LocateFfprobe());
            Assert.True(locator.IsFfmpegAvailable);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsFfmpegAvailable_ReturnsFalse_WhenOverridesDoNotExist()
    {
        var nonExistentFfmpeg = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N") + ".exe");
        var nonExistentFfprobe = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N") + ".exe");

        var locator = new TestableFfmpegLocator(overrideFfmpeg: nonExistentFfmpeg, overrideFfprobe: nonExistentFfprobe, forceNoFallback: true);

        Assert.False(locator.IsFfmpegAvailable);
        Assert.Null(locator.LocateFfmpeg());
        Assert.Null(locator.LocateFfprobe());
    }

    [Fact]
    public void IsFfmpegAvailable_ReturnsFalse_WhenOnlyFfmpegFound()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "QuickConvert_LocatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fakeFfmpeg = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(fakeFfmpeg, "dummy");

            var locator = new TestableFfmpegLocator(overrideFfmpeg: fakeFfmpeg, overrideFfprobe: null, forceNoFallback: true);

            Assert.False(locator.IsFfmpegAvailable);
            Assert.Equal(fakeFfmpeg, locator.LocateFfmpeg());
            Assert.Null(locator.LocateFfprobe());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class TestableFfmpegLocator : FfmpegLocator
    {
        private readonly string? _customFfmpeg;
        private readonly string? _customFfprobe;
        private readonly bool _forceNoFallback;

        public TestableFfmpegLocator(string? overrideFfmpeg, string? overrideFfprobe, bool forceNoFallback)
            : base(overrideFfmpeg, overrideFfprobe)
        {
            _customFfmpeg = overrideFfmpeg;
            _customFfprobe = overrideFfprobe;
            _forceNoFallback = forceNoFallback;
        }

        public override string? LocateFfmpeg()
        {
            if (!string.IsNullOrEmpty(_customFfmpeg) && File.Exists(_customFfmpeg))
                return _customFfmpeg;
            return _forceNoFallback ? null : base.LocateFfmpeg();
        }

        public override string? LocateFfprobe()
        {
            if (!string.IsNullOrEmpty(_customFfprobe) && File.Exists(_customFfprobe))
                return _customFfprobe;
            return _forceNoFallback ? null : base.LocateFfprobe();
        }
    }
}
