namespace QuickConvert.Engines.Media;

/// <summary>
/// Probed capabilities and hardware acceleration encoders detected from FFmpeg.
/// </summary>
public class FfmpegCapabilities
{
    public static FfmpegCapabilities Unavailable { get; } = new(
        isAvailable: false,
        version: null,
        ffmpegPath: null,
        ffprobePath: null,
        allEncoders: Array.Empty<string>(),
        hardwareEncoders: Array.Empty<string>());

    public bool IsAvailable { get; }
    public string? Version { get; }
    public string? FfmpegPath { get; }
    public string? FfprobePath { get; }
    public IReadOnlySet<string> AllEncoders { get; }
    public IReadOnlySet<string> HardwareEncoders { get; }

    public string PreferredH264Encoder { get; }
    public string PreferredHevcEncoder { get; }

    public FfmpegCapabilities(
        bool isAvailable,
        string? version,
        string? ffmpegPath,
        string? ffprobePath,
        IEnumerable<string> allEncoders,
        IEnumerable<string> hardwareEncoders)
    {
        IsAvailable = isAvailable;
        Version = version;
        FfmpegPath = ffmpegPath;
        FfprobePath = ffprobePath;

        var allSet = new HashSet<string>(allEncoders, StringComparer.OrdinalIgnoreCase);
        AllEncoders = allSet;

        var hwSet = new HashSet<string>(hardwareEncoders, StringComparer.OrdinalIgnoreCase);
        HardwareEncoders = hwSet;

        PreferredH264Encoder = SelectPreferredH264(hwSet, allSet);
        PreferredHevcEncoder = SelectPreferredHevc(hwSet, allSet);
    }

    public bool HasEncoder(string encoderName) => AllEncoders.Contains(encoderName);

    public bool HasHardwareEncoder(string encoderName) => HardwareEncoders.Contains(encoderName);

    private static string SelectPreferredH264(HashSet<string> hw, HashSet<string> all)
    {
        if (hw.Contains("h264_nvenc")) return "h264_nvenc";
        if (hw.Contains("h264_qsv")) return "h264_qsv";
        if (hw.Contains("h264_amf")) return "h264_amf";
        return all.Contains("libx264") ? "libx264" : "libx264";
    }

    private static string SelectPreferredHevc(HashSet<string> hw, HashSet<string> all)
    {
        if (hw.Contains("hevc_nvenc")) return "hevc_nvenc";
        if (hw.Contains("hevc_qsv")) return "hevc_qsv";
        if (hw.Contains("hevc_amf")) return "hevc_amf";
        return all.Contains("libx265") ? "libx265" : "libx265";
    }
}
