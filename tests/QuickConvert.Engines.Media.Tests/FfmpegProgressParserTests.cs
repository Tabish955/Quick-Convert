using Xunit;

namespace QuickConvert.Engines.Media.Tests;

public class FfmpegProgressParserTests
{
    [Fact]
    public void ProcessLine_ParsesOutTimeUs_AndScalesTo95Percent()
    {
        var reported = new List<double>();
        var progress = new Progress<double>(reported.Add);

        // 10 second total duration
        var parser = new FfmpegProgressParser(10.0, progress);

        // 5 seconds elapsed (5,000,000 us) -> 50% of 95% = 47.5%
        var percent = parser.ProcessLine("out_time_us=5000000");

        Assert.NotNull(percent);
        Assert.Equal(47.5, percent.Value, precision: 1);
    }

    [Fact]
    public void ProcessLine_ParsesOutTime_FormattedString()
    {
        var reported = new List<double>();
        var progress = new Progress<double>(reported.Add);

        // 20 second total duration
        var parser = new FfmpegProgressParser(20.0, progress);

        // 10 seconds elapsed -> 47.5%
        var percent = parser.ProcessLine("out_time=00:00:10.000000");

        Assert.NotNull(percent);
        Assert.Equal(47.5, percent.Value, precision: 1);
    }

    [Fact]
    public void ProcessLine_ProgressEnd_Reports95Percent()
    {
        var reported = new List<double>();
        var progress = new Progress<double>(reported.Add);

        var parser = new FfmpegProgressParser(30.0, progress);

        var percent = parser.ProcessLine("progress=end");

        Assert.NotNull(percent);
        Assert.Equal(95.0, percent.Value);
    }

    [Fact]
    public void ProcessLine_ZeroDuration_SuppressesProgress()
    {
        var reported = new List<double>();
        var progress = new Progress<double>(reported.Add);

        var parser = new FfmpegProgressParser(0.0, progress);

        var percent = parser.ProcessLine("out_time_us=5000000");

        Assert.Null(percent);
        Assert.Empty(reported);
    }
}
