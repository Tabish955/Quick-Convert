using System.Diagnostics;
using QuickConvert.Core.Diagnostics;

namespace QuickConvert.Engines.Media;

/// <summary>
/// Production subprocess runner for FFmpeg and FFprobe executions.
/// Strictly uses ProcessStartInfo.ArgumentList (no shell concatenation), enforces
/// process tree termination on cancellation, captures up to 50 stderr lines for
/// failure diagnostics, and terminates frozen encodes via idle timeout.
/// </summary>
public class FfmpegProcessRunner : IFfmpegProcessRunner
{
    private static readonly string[] DefaultFfmpegPrefix =
        ["-hide_banner", "-nostdin", "-y", "-nostats", "-loglevel", "error"];

    private readonly object _stderrLock = new();
    private readonly Queue<string> _recentStderr = new(50);
    private readonly FfmpegLocator? _locator;

    public FfmpegProcessRunner(FfmpegLocator? locator = null)
    {
        _locator = locator;
    }

    public IReadOnlyList<string> RecentStderr
    {
        get
        {
            lock (_stderrLock)
            {
                return _recentStderr.ToList();
            }
        }
    }

    public string GetRecentStderrText()
    {
        lock (_stderrLock)
        {
            return string.Join(Environment.NewLine, _recentStderr);
        }
    }

    public Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onStdoutLine,
        IProgress<string>? onStderrLine,
        CancellationToken ct,
        TimeSpan idleTimeout)
    {
        var locator = _locator ?? new FfmpegLocator();
        var ffmpegPath = locator.LocateFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new FileNotFoundException("FFmpeg executable could not be located. Run tools/Get-Ffmpeg.ps1 to install it.");
        }

        return RunAsync(ffmpegPath, arguments, workingDirectory, onStdoutLine, onStderrLine, ct, idleTimeout);
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
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path must be specified.", nameof(executablePath));
        }

        lock (_stderrLock)
        {
            _recentStderr.Clear();
        }

        ct.ThrowIfCancellationRequested();

        var finalArgs = PrepareArguments(executablePath, arguments);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in finalArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var activityLock = new object();
        var lastActivityUtc = DateTime.UtcNow;

        void TouchActivity()
        {
            lock (activityLock)
            {
                lastActivityUtc = DateTime.UtcNow;
            }
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                TouchActivity();
                onStdoutLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                TouchActivity();
                RecordStderrLine(e.Data);
                onStderrLine?.Report(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start subprocess: {executablePath}");
        }

        try
        {
            process.StandardInput.Close();
        }
        catch { }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var watchdogCts = new CancellationTokenSource();
        var timeoutTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watchdogTask = Task.Run(async () =>
        {
            var checkInterval = TimeSpan.FromMilliseconds(250);
            while (!watchdogCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(checkInterval, watchdogCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                TimeSpan idle;
                lock (activityLock)
                {
                    idle = DateTime.UtcNow - lastActivityUtc;
                }

                if (idle > idleTimeout)
                {
                    QuickConvertLogger.Warn($"FFmpegProcessRunner: idle timeout ({idle.TotalSeconds:F1}s > {idleTimeout.TotalSeconds:F1}s) reached for PID {process.Id}. Killing process tree.");
                    KillProcessTree(process);
                    timeoutTcs.TrySetResult(true);
                    break;
                }
            }
        }, watchdogCts.Token);

        try
        {
            var processExitTask = process.WaitForExitAsync(CancellationToken.None);
            var cancellationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = ct.Register(() => cancellationTcs.TrySetResult(true));

            var finished = await Task.WhenAny(processExitTask, timeoutTcs.Task, cancellationTcs.Task).ConfigureAwait(false);

            if (finished == cancellationTcs.Task)
            {
                QuickConvertLogger.Info($"FFmpegProcessRunner: cancellation requested for PID {process.Id}. Terminating process tree.");
                KillProcessTree(process);
                ct.ThrowIfCancellationRequested();
            }

            if (finished == timeoutTcs.Task)
            {
                throw new TimeoutException($"Subprocess execution timed out after {idleTimeout.TotalSeconds:F1}s of inactivity without output.");
            }

            await processExitTask.ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            watchdogCts.Cancel();
            try { await watchdogTask.ConfigureAwait(false); } catch { }
        }
    }

    internal static IReadOnlyList<string> PrepareArguments(string executablePath, IReadOnlyList<string> arguments)
    {
        var exeName = Path.GetFileNameWithoutExtension(executablePath);
        if (exeName.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.Count > 0 && arguments[0] == "-hide_banner")
            {
                return arguments;
            }

            var merged = new List<string>(DefaultFfmpegPrefix.Length + arguments.Count);
            merged.AddRange(DefaultFfmpegPrefix);
            merged.AddRange(arguments);
            return merged;
        }

        return arguments;
    }

    internal void RecordStderrLine(string line)
    {
        lock (_stderrLock)
        {
            if (_recentStderr.Count >= 50)
            {
                _recentStderr.Dequeue();
            }
            _recentStderr.Enqueue(line);
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                bool exited = process.WaitForExit(5000);
                if (!exited)
                {
                    QuickConvertLogger.Warn($"Subprocess {process.Id} (tree) did not exit within 5 seconds of Kill.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited normally
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Failed to kill subprocess {process.Id}: {ex.Message}");
        }
    }
}
