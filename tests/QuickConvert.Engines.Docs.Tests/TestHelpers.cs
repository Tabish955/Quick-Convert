using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qc-p4-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string Sub(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

/// <summary>Records invocations; optionally produces output files like a real tool would.</summary>
public sealed class FakeRunner : IToolProcessRunner
{
    public List<(string Exe, string[] Args)> Calls { get; } = new();
    public Func<string, string[], ProcessResult>? Behaviour { get; set; }
    public Action<string, string[]>? OnRun { get; set; }

    public Task<ProcessResult> RunAsync(string exePath, IReadOnlyList<string> args, string? workingDirectory, Action<string>? onStdoutLine, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var a = args.ToArray();
        Calls.Add((exePath, a));
        OnRun?.Invoke(exePath, a);
        return Task.FromResult(Behaviour?.Invoke(exePath, a) ?? new ProcessResult(0, string.Empty));
    }

    /// <summary>The value following "-o" (pandoc) or the last argument (wkhtmltopdf).</summary>
    public static string OutputOf(string[] a)
    {
        var i = Array.IndexOf(a, "-o");
        return i >= 0 ? a[i + 1] : a[^1];
    }
}

public static class ZipFactory
{
    public static string MakeZip(string path, params (string name, byte[] data)[] entries)
    {
        using var fs = File.Create(path);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var e = zip.CreateEntry(name); // CreateEntry accepts hostile names like "../x"; extraction code must not.
            using var s = e.Open();
            s.Write(data);
        }
        return path;
    }

    public static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    public static byte[] Random(int length, int seed)
    {
        var b = new byte[length];
        new Random(seed).NextBytes(b);
        return b;
    }
}
