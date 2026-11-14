using System.Collections.Concurrent;
using System.Diagnostics;

namespace QuickConvert.Engines.Docs.Tools;

public sealed record ProcessResult(int ExitCode, string StdErrTail);

public interface IToolProcessRunner
{
    Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, string? workingDirectory,
        Action<string>? onStdoutLine, CancellationToken ct);
}

/// <summary>ArgumentList only (no shell, no string concatenation), hidden window, stderr tail, tree kill on cancel.</summary>
public sealed class ToolProcessRunner : IToolProcessRunner
{
    private const int StderrLines = 50;

    public async Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, string? workingDirectory,
        Action<string>? onStdoutLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        foreach (var a in args) psi.ArgumentList.Add(a);

        var stderr = new ConcurrentQueue<string>();
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) onStdoutLine?.Invoke(e.Data); };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.Enqueue(e.Data);
            while (stderr.Count > StderrLines) stderr.TryDequeue(out var _);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() => TryKill(proc));
        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            try { await proc.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false); }
            catch { /* grace period over */ }
            throw;
        }

        proc.WaitForExit(); // flush async readers
        return new ProcessResult(proc.ExitCode, string.Join(Environment.NewLine, stderr));
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }
}
