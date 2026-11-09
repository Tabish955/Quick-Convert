namespace QuickConvert.Engines.Media.Tests;

public record RunInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public class FakeFfmpegProcessRunner : IFfmpegProcessRunner
{
    private readonly List<RunInvocation> _invocations = new();
    private readonly List<string> _recentStderr = new();

    public IReadOnlyList<RunInvocation> Invocations => _invocations;
    public IReadOnlyList<string> RecentStderr => _recentStderr;

    public Func<string, IReadOnlyList<string>, int> ExitCodeSelector { get; set; } = (_, _) => 0;
    public Action<Action<string>?, IProgress<string>?>? OutputEmitter { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public bool SimulateTimeout { get; set; }

    public string GetRecentStderrText() => string.Join(Environment.NewLine, _recentStderr);

    public Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onStdoutLine,
        IProgress<string>? onStderrLine,
        CancellationToken ct,
        TimeSpan idleTimeout)
    {
        return RunAsync("ffmpeg.exe", arguments, workingDirectory, onStdoutLine, onStderrLine, ct, idleTimeout);
    }

    public async Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onStdoutLine,
        IProgress<string>? onStderrLine,
        CancellationToken ct,
        TimeSpan idleTimeout)
    {
        _invocations.Add(new RunInvocation(executablePath, arguments, workingDirectory));

        if (SimulateTimeout)
        {
            throw new TimeoutException($"Subprocess execution timed out after {idleTimeout.TotalSeconds:F1}s of inactivity without output.");
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, ct);
        }

        ct.ThrowIfCancellationRequested();

        OutputEmitter?.Invoke(onStdoutLine, onStderrLine);

        int exitCode = ExitCodeSelector(executablePath, arguments);
        return exitCode;
    }
}
