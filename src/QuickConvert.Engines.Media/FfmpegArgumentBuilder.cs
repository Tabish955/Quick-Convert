using QuickConvert.Core.Services;

namespace QuickConvert.Engines.Media;

/// <summary>
/// Pure functional argument builder translating media conversion requests into FFmpeg argument lists.
/// </summary>
public static class FfmpegArgumentBuilder
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma"
    };

    /// <summary>
    /// Builds an argument list for FFmpeg execution.
    /// Note: Standard flags (-hide_banner, -nostdin, -y, etc.) are managed by FfmpegProcessRunner.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        string sourceExt,
        string targetExt,
        string? preferredVideoEncoder = null)
    {
        var normSource = FormatRegistry.NormalizeExtension(sourceExt);
        var normTarget = FormatRegistry.NormalizeExtension(targetExt);

        var args = new List<string>
        {
            "-i", inputPath
        };

        bool sourceIsVideo = VideoExtensions.Contains(normSource);
        bool targetIsAudio = AudioExtensions.Contains(normTarget);

        if (normTarget == ".gif")
        {
            // Video to GIF with two-pass palette generation for high visual fidelity
            args.AddRange(new[]
            {
                "-filter_complex",
                "[0:v] fps=15,scale=w=480:h=-1:flags=lanczos,split [a][b];[a] palettegen [p];[b][p] paletteuse"
            });
        }
        else if (sourceIsVideo && targetIsAudio)
        {
            // Video to Audio: strip video stream and transcode audio
            args.Add("-vn");
            AppendAudioEncoderArgs(args, normTarget);
        }
        else if (!sourceIsVideo && targetIsAudio)
        {
            // Audio to Audio
            AppendAudioEncoderArgs(args, normTarget);
        }
        else if (sourceIsVideo && !targetIsAudio)
        {
            // Video to Video
            AppendVideoEncoderArgs(args, normSource, normTarget, preferredVideoEncoder);
        }
        else
        {
            // Fallback default
            AppendAudioEncoderArgs(args, normTarget);
        }

        args.Add("-progress");
        args.Add("pipe:1");
        args.Add(outputPath);

        return args;
    }

    private static void AppendAudioEncoderArgs(List<string> args, string targetExt)
    {
        switch (targetExt)
        {
            case ".mp3":
                args.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", "192k" });
                break;
            case ".wav":
                args.AddRange(new[] { "-c:a", "pcm_s16le" });
                break;
            case ".m4a":
            case ".aac":
                args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
                break;
            case ".flac":
                args.AddRange(new[] { "-c:a", "flac" });
                break;
            case ".ogg":
                args.AddRange(new[] { "-c:a", "libvorbis", "-b:a", "192k" });
                break;
            default:
                args.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", "192k" });
                break;
        }
    }

    private static void AppendVideoEncoderArgs(
        List<string> args,
        string sourceExt,
        string targetExt,
        string? preferredVideoEncoder)
    {
        if (targetExt == ".webm")
        {
            args.AddRange(new[]
            {
                "-c:v", "libvpx-vp9",
                "-crf", "32",
                "-b:v", "0",
                "-c:a", "libopus"
            });
            return;
        }

        if (targetExt == ".mkv" && (sourceExt == ".mp4" || sourceExt == ".mkv"))
        {
            // Matroska container stream copy
            args.AddRange(new[] { "-c", "copy" });
            return;
        }

        if (targetExt == ".avi")
        {
            args.AddRange(new[]
            {
                "-c:v", "mpeg4",
                "-q:v", "3",
                "-c:a", "libmp3lame",
                "-b:a", "192k"
            });
            return;
        }

        // Target .mp4, .mov, or transcoded .mkv
        var videoEnc = preferredVideoEncoder;
        if (string.IsNullOrWhiteSpace(videoEnc) || videoEnc.Equals("libx264", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[]
            {
                "-c:v", "libx264",
                "-crf", "20",
                "-preset", "medium",
                "-c:a", "aac"
            });
        }
        else if (videoEnc.Equals("h264_nvenc", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[]
            {
                "-c:v", "h264_nvenc",
                "-cq", "20",
                "-c:a", "aac"
            });
        }
        else if (videoEnc.Equals("h264_qsv", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[]
            {
                "-c:v", "h264_qsv",
                "-global_quality", "20",
                "-c:a", "aac"
            });
        }
        else if (videoEnc.Equals("h264_amf", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[]
            {
                "-c:v", "h264_amf",
                "-rc", "cqp",
                "-qp_p", "20",
                "-c:a", "aac"
            });
        }
        else
        {
            // Generic hardware encoder fallback
            args.AddRange(new[]
            {
                "-c:v", videoEnc,
                "-c:a", "aac"
            });
        }
    }
}
