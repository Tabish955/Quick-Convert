using Xunit;

namespace QuickConvert.Engines.Media.Tests;

public class FfmpegArgumentBuilderTests
{
    [Fact]
    public void BuildArguments_Mp4ToMp3_GeneratesCorrectArgs()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("input.mp4", "output.mp3", ".mp4", ".mp3");

        Assert.Equal(new[]
        {
            "-i", "input.mp4",
            "-vn",
            "-c:a", "libmp3lame",
            "-b:a", "192k",
            "-progress", "pipe:1",
            "output.mp3"
        }, args);
    }

    [Fact]
    public void BuildArguments_VideoToWav_GeneratesCorrectArgs()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("input.mkv", "output.wav", ".mkv", ".wav");

        Assert.Equal(new[]
        {
            "-i", "input.mkv",
            "-vn",
            "-c:a", "pcm_s16le",
            "-progress", "pipe:1",
            "output.wav"
        }, args);
    }

    [Fact]
    public void BuildArguments_VideoToM4a_GeneratesCorrectArgs()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("input.mov", "output.m4a", ".mov", ".m4a");

        Assert.Equal(new[]
        {
            "-i", "input.mov",
            "-vn",
            "-c:a", "aac",
            "-b:a", "192k",
            "-progress", "pipe:1",
            "output.m4a"
        }, args);
    }

    [Fact]
    public void BuildArguments_Mp4ToWebm_GeneratesCorrectArgs()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("input.mp4", "output.webm", ".mp4", ".webm");

        Assert.Equal(new[]
        {
            "-i", "input.mp4",
            "-c:v", "libvpx-vp9",
            "-crf", "32",
            "-b:v", "0",
            "-c:a", "libopus",
            "-progress", "pipe:1",
            "output.webm"
        }, args);
    }

    [Fact]
    public void BuildArguments_WebmToMp4_SoftwareEncoder_GeneratesCorrectArgs()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("input.webm", "output.mp4", ".webm", ".mp4", "libx264");

        Assert.Equal(new[]
        {
            "-i", "input.webm",
            "-c:v", "libx264",
            "-crf", "20",
            "-preset", "medium",
            "-c:a", "aac",
            "-progress", "pipe:1",
            "output.mp4"
        }, args);
    }

    [Fact]
    public void BuildArguments_WebmToMp4_NvencHardwareEncoder_GeneratesCorrectArgs()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("input.webm", "output.mp4", ".webm", ".mp4", "h264_nvenc");

        Assert.Equal(new[]
        {
            "-i", "input.webm",
            "-c:v", "h264_nvenc",
            "-cq", "20",
            "-c:a", "aac",
            "-progress", "pipe:1",
            "output.mp4"
        }, args);
    }

    [Fact]
    public void BuildArguments_VideoToGif_GeneratesTwoPassPaletteFilter()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("clip.mp4", "clip.gif", ".mp4", ".gif");

        Assert.Equal(new[]
        {
            "-i", "clip.mp4",
            "-filter_complex",
            "[0:v] fps=15,scale=w=480:h=-1:flags=lanczos,split [a][b];[a] palettegen [p];[b][p] paletteuse",
            "-progress", "pipe:1",
            "clip.gif"
        }, args);
    }

    [Fact]
    public void BuildArguments_WavToMp3_GeneratesCorrectArgs()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("audio.wav", "audio.mp3", ".wav", ".mp3");

        Assert.Equal(new[]
        {
            "-i", "audio.wav",
            "-c:a", "libmp3lame",
            "-b:a", "192k",
            "-progress", "pipe:1",
            "audio.mp3"
        }, args);
    }

    [Fact]
    public void BuildArguments_Mp4ToMkv_ContainerStreamCopy()
    {
        var args = FfmpegArgumentBuilder.BuildArguments("video.mp4", "video.mkv", ".mp4", ".mkv");

        Assert.Equal(new[]
        {
            "-i", "video.mp4",
            "-c", "copy",
            "-progress", "pipe:1",
            "video.mkv"
        }, args);
    }
}
