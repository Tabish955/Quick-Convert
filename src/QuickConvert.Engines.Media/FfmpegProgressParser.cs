using System.Globalization;

namespace QuickConvert.Engines.Media;

/// <summary>
/// Parses FFmpeg machine-readable progress output (-progress pipe:1)
/// and translates output time into normalized progress percentages (0.0 to 95.0).
/// </summary>
public class FfmpegProgressParser
{
    private readonly double _totalDurationSeconds;
    private readonly IProgress<double>? _progress;

    public FfmpegProgressParser(double totalDurationSeconds, IProgress<double>? progress)
    {
        _totalDurationSeconds = totalDurationSeconds;
        _progress = progress;
    }

    /// <summary>
    /// Processes a single stdout line from FFmpeg.
    /// Returns the computed percentage (0..95), or null if the line didn't update progress.
    /// </summary>
    public double? ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var parts = line.Split('=', 2);
        if (parts.Length != 2) return null;

        var key = parts[0].Trim();
        var val = parts[1].Trim();

        if (key.Equals("progress", StringComparison.OrdinalIgnoreCase))
        {
            if (val.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                _progress?.Report(95.0);
                return 95.0;
            }
            return null;
        }

        if (_totalDurationSeconds <= 0.0) return null;

        double? currentSeconds = null;

        if (key.Equals("out_time_us", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long us) && us >= 0)
            {
                currentSeconds = us / 1_000_000.0;
            }
        }
        else if (key.Equals("out_time_ms", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms) && ms >= 0)
            {
                // In some ffmpeg versions, out_time_ms is actually in microseconds; if very large (> 1e7), treat as us
                currentSeconds = ms > 10_000_000 ? ms / 1_000_000.0 : ms / 1_000.0;
            }
        }
        else if (key.Equals("out_time", StringComparison.OrdinalIgnoreCase))
        {
            if (TimeSpan.TryParse(val, CultureInfo.InvariantCulture, out var ts))
            {
                currentSeconds = ts.TotalSeconds;
            }
        }

        if (currentSeconds.HasValue)
        {
            double fraction = Math.Clamp(currentSeconds.Value / _totalDurationSeconds, 0.0, 1.0);
            double percent = fraction * 95.0;
            _progress?.Report(percent);
            return percent;
        }

        return null;
    }
}
