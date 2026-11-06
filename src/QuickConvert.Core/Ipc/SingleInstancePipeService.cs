using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using QuickConvert.Core.Models;

namespace QuickConvert.Core.Ipc;

/// <summary>
/// Event arguments holding files and cursor coordinates received from File Explorer.
/// </summary>
public class FilesReceivedEventArgs : EventArgs
{
    public IReadOnlyList<string> FilePaths { get; }
    public ScreenPoint CursorPosition { get; }

    public FilesReceivedEventArgs(IReadOnlyList<string> filePaths, ScreenPoint cursorPosition)
    {
        FilePaths = filePaths;
        CursorPosition = cursorPosition;
    }
}

/// <summary>
/// IPC payload transmitted over Named Pipe between secondary and primary instances.
/// </summary>
internal class PipeTransferPayload
{
    public List<string> FilePaths { get; set; } = new();
    public double CursorX { get; set; }
    public double CursorY { get; set; }
}

/// <summary>
/// Single-instance mutex manager and Named Pipe IPC server/client.
/// Aggregates Explorer multi-file launches into a single debounced batch event.
/// </summary>
public class SingleInstancePipeService : IDisposable
{
    public const string DefaultMutexName = @"Global\QuickConvert_SingleInstance_Mutex";
    public const string FallbackMutexName = @"Local\QuickConvert_SingleInstance_Mutex";
    public const string PipeName = "QuickConvert_Explorer_Pipe";

    private Mutex? _mutex;
    private bool _hasMutex;
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;

    // Buffer and debounce timer for incoming explorer requests
    private readonly ConcurrentBag<string> _pendingFilePaths = new();
    private readonly Timer _debounceTimer;
    private ScreenPoint _lastKnownCursorPosition;
    private readonly object _timerLock = new();

    public event EventHandler<FilesReceivedEventArgs>? FilesReceivedFromExplorer;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public SingleInstancePipeService()
    {
        _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Captures the current mouse cursor screen coordinates.
    /// </summary>
    public static ScreenPoint GetCursorPosition()
    {
        if (GetCursorPos(out var pt))
        {
            return new ScreenPoint(pt.X, pt.Y);
        }
        return new ScreenPoint(0, 0);
    }

    /// <summary>
    /// Checks if this process is the primary instance.
    /// Returns true if primary; false if a primary instance already exists.
    /// </summary>
    public bool TryAcquireSingleInstance()
    {
        try
        {
            _mutex = new Mutex(true, DefaultMutexName, out _hasMutex);
        }
        catch (UnauthorizedAccessException)
        {
            _mutex = new Mutex(true, FallbackMutexName, out _hasMutex);
        }
        catch (Exception)
        {
            _mutex = new Mutex(true, FallbackMutexName, out _hasMutex);
        }

        return _hasMutex;
    }

    /// <summary>
    /// Sends command-line file paths from a secondary instance to the primary instance via Named Pipe.
    /// Exits the current process upon completion if exitOnSent is true.
    /// </summary>
    public static async Task<bool> SendFilesToPrimaryAsync(
        IEnumerable<string> filePaths,
        int timeoutMs = 300,
        bool exitOnSent = true)
    {
        var cursor = GetCursorPosition();
        var payload = new PipeTransferPayload
        {
            FilePaths = filePaths.Where(f => !string.IsNullOrWhiteSpace(f)).ToList(),
            CursorX = cursor.X,
            CursorY = cursor.Y
        };

        if (payload.FilePaths.Count == 0)
        {
            if (exitOnSent) Environment.Exit(0);
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            using var connectCts = new CancellationTokenSource(timeoutMs);

            await client.ConnectAsync(connectCts.Token);

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            var lengthPrefix = BitConverter.GetBytes(jsonBytes.Length);

            await client.WriteAsync(lengthPrefix, 0, lengthPrefix.Length, connectCts.Token);
            await client.WriteAsync(jsonBytes, 0, jsonBytes.Length, connectCts.Token);
            await client.FlushAsync(connectCts.Token);

            if (exitOnSent)
            {
                Environment.Exit(0);
            }
            return true;
        }
        catch
        {
            if (exitOnSent)
            {
                Environment.Exit(0);
            }
            return false;
        }
    }

    /// <summary>
    /// Starts the Named Pipe server loop listening for secondary instance transmissions.
    /// </summary>
    public void StartPipeServer()
    {
        if (_serverTask != null) return;

        _serverCts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoopAsync(_serverCts.Token));
    }

    private async Task ServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                // Handle incoming connection in a separate detached task so server immediately listens again
                _ = HandleClientConnectionAsync(server, ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch
            {
                server?.Dispose();
                await Task.Delay(50, ct);
            }
        }
    }

    private async Task HandleClientConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        using (server)
        {
            try
            {
                var lengthBuffer = new byte[sizeof(int)];
                int bytesRead = await server.ReadAsync(lengthBuffer, 0, sizeof(int), ct);
                if (bytesRead < sizeof(int)) return;

                int payloadLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (payloadLength <= 0 || payloadLength > 10 * 1024 * 1024) return; // 10MB safety sanity check

                var payloadBuffer = new byte[payloadLength];
                int totalRead = 0;
                while (totalRead < payloadLength)
                {
                    int chunk = await server.ReadAsync(payloadBuffer, totalRead, payloadLength - totalRead, ct);
                    if (chunk == 0) break;
                    totalRead += chunk;
                }

                if (totalRead == payloadLength)
                {
                    var payload = JsonSerializer.Deserialize<PipeTransferPayload>(payloadBuffer);
                    if (payload != null && payload.FilePaths != null && payload.FilePaths.Count > 0)
                    {
                        foreach (var path in payload.FilePaths)
                        {
                            if (!string.IsNullOrWhiteSpace(path))
                            {
                                _pendingFilePaths.Add(path);
                            }
                        }

                        lock (_timerLock)
                        {
                            _lastKnownCursorPosition = new ScreenPoint(payload.CursorX, payload.CursorY);
                            // 150ms debounce window as specified in prompt
                            _debounceTimer.Change(150, Timeout.Infinite);
                        }
                    }
                }
            }
            catch
            {
                // Ignore transient pipe read failures
            }
        }
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_pendingFilePaths.TryTake(out var path))
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                collected.Add(path);
            }
        }

        if (collected.Count == 0) return;

        ScreenPoint cursor;
        lock (_timerLock)
        {
            cursor = _lastKnownCursorPosition.X != 0 || _lastKnownCursorPosition.Y != 0
                ? _lastKnownCursorPosition
                : GetCursorPosition();
        }

        FilesReceivedFromExplorer?.Invoke(this, new FilesReceivedEventArgs(collected.ToList(), cursor));
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        _serverCts?.Dispose();
        _debounceTimer.Dispose();

        if (_hasMutex && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            _mutex.Dispose();
            _hasMutex = false;
        }
    }
}
