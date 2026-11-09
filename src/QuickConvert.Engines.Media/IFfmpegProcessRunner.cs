namespace QuickConvert.Engines.Media;

/// <summary>
/// Contract for executing FFmpeg / FFprobe subprocesses with argument list isolation,
/// stream redirection, idle timeout watchdog, and process tree termination.
/// </summary>
public interface IFfmpegProcessRunner
{
    /// <summary>
    /// Gets the most recent stderr lines captured during execution (up to 50 lines).
    /// </summary>
    IReadOnlyList<string> RecentStderr { get; }

    /// <summary>
    /// Formatted text of the most recent captured stderr lines.
    /// </summary>
    string GetRecentStderrText();

    /// <summary>
    /// Executes a process with direct argument list, real-time output callbacks, cancellation, and idle timeout.
    /// </summary>
    /// <param name="executablePath">Absolute or resolved path to ffmpeg.exe or ffprobe.exe.</param>
    /// <param name="arguments">Command line arguments passed as an explicit list without shell expansion.</param>
    /// <param name="workingDirectory">Working directory for the child process.</param>
    /// <param name="onStdoutLine">Callback invoked for each line written to standard output.</param>
    /// <param name="onStderrLine">Progress reporter invoked for lines written to standard error.</param>
    /// <param name="ct">Token to cancel execution and terminate the child process tree.</param>
    /// <param name="idleTimeout">Maximum duration with zero output before the process tree is aborted.</param>
    /// <returns>Exit code of the process (0 indicates success).</returns>
    Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onStdoutLine,
        IProgress<string>? onStderrLine,
        CancellationToken ct,
        TimeSpan idleTimeout);

    /// <summary>
    /// Overload that executes using the default resolved ffmpeg.exe binary.
    /// </summary>
    Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onStdoutLine,
        IProgress<string>? onStderrLine,
        CancellationToken ct,
        TimeSpan idleTimeout);
}
