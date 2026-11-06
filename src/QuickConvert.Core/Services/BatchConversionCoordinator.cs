using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;

namespace QuickConvert.Core.Services;

/// <summary>
/// Coordinates batch conversion operations, manages concurrency via SemaphoreSlim,
/// tracks job states, and handles cancellation.
/// </summary>
public class BatchConversionCoordinator
{
    private readonly IConversionEngine _engine;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobCts = new();
    private CancellationTokenSource? _batchCts;
    private readonly SynchronizationContext? _syncContext;

    private int _concurrencyLimit;
    private bool _isConverting;

    public ObservableCollection<ConversionJob> Jobs { get; } = new();

    public bool IsConverting
    {
        get => _isConverting;
        private set
        {
            _isConverting = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int ActiveJobsCount => Jobs.Count(j => j.Status == ConversionStatus.Converting || j.Status == ConversionStatus.Queued);
    public int CompletedJobsCount => Jobs.Count(j => j.Status == ConversionStatus.Completed);
    public int TotalJobsCount => Jobs.Count;

    public double OverallProgress
    {
        get
        {
            if (Jobs.Count == 0) return 0;
            return Math.Round(Jobs.Average(j => j.ProgressPercentage), 1);
        }
    }

    public event EventHandler? StateChanged;
    public event EventHandler<ConversionJob>? JobCompleted;
    public event EventHandler? BatchCompleted;

    public BatchConversionCoordinator(IConversionEngine? engine = null)
    {
        _engine = engine ?? new MockConversionEngine();
        _concurrencyLimit = Math.Max(1, Environment.ProcessorCount / 2);
        _semaphore = new SemaphoreSlim(_concurrencyLimit, _concurrencyLimit);
        _syncContext = SynchronizationContext.Current;
    }

    /// <summary>
    /// Enqueues multiple file paths into the batch queue.
    /// Infers file category, size, and sets recommended target format if not supplied.
    /// </summary>
    public IReadOnlyList<ConversionJob> EnqueueFiles(IEnumerable<string> filePaths, string? defaultTargetExt = null)
    {
        var addedJobs = new List<ConversionJob>();

        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            // Avoid duplicate path already pending, queued or actively converting
            if (Jobs.Any(j => j.InputFilePath.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                              (j.Status == ConversionStatus.Pending ||
                               j.Status == ConversionStatus.Queued ||
                               j.Status == ConversionStatus.Converting)))
            {
                continue;
            }

            var job = ConversionJob.FromFilePath(path, defaultTargetExt);
            job.Category = FormatRegistry.GetCategory(job.SourceExtension);

            if (string.IsNullOrWhiteSpace(job.TargetExtension))
            {
                var supported = FormatRegistry.GetSupportedTargetFormats(job.SourceExtension);
                job.TargetExtension = supported.Count > 0 ? supported[0] : string.Empty;
            }

            // Hook property changed on job to trigger overall progress updates
            job.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ConversionJob.ProgressPercentage) ||
                    e.PropertyName == nameof(ConversionJob.Status))
                {
                    Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
                }
            };

            Dispatch(() => Jobs.Add(job));
            addedJobs.Add(job);
        }

        Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
        return addedJobs;
    }

    /// <summary>
    /// Starts execution of all pending or queued jobs concurrently using SemaphoreSlim.
    /// </summary>
    public async Task StartAllAsync(ConversionOptions? options = null)
    {
        if (IsConverting) return;

        var pendingJobs = Jobs.Where(j => j.Status == ConversionStatus.Pending || j.Status == ConversionStatus.Queued).ToList();
        if (pendingJobs.Count == 0) return;

        IsConverting = true;
        _batchCts?.Dispose();
        _batchCts = new CancellationTokenSource();

        foreach (var job in pendingJobs)
        {
            job.Status = ConversionStatus.Queued;
            job.StatusText = "Queued";
        }

        var tasks = pendingJobs.Select(job => ProcessJobAsync(job, options, _batchCts.Token)).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        finally
        {
            IsConverting = false;
            Dispatch(() =>
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
                BatchCompleted?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private async Task ProcessJobAsync(ConversionJob job, ConversionOptions? options, CancellationToken masterCt)
    {
        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(masterCt);
        _jobCts[job.Id] = jobCts;

        await _semaphore.WaitAsync(masterCt);
        try
        {
            if (jobCts.Token.IsCancellationRequested)
            {
                job.Status = ConversionStatus.Cancelled;
                job.StatusText = "Cancelled";
                return;
            }

            var jobOptions = options ?? new ConversionOptions
            {
                TargetExtension = job.TargetExtension,
                Quality = 85
            };

            // Ensure job's specific target format is respected if options has blank target
            if (string.IsNullOrWhiteSpace(jobOptions.TargetExtension) && !string.IsNullOrWhiteSpace(job.TargetExtension))
            {
                jobOptions = jobOptions with { TargetExtension = job.TargetExtension };
            }

            var progress = new Progress<double>(p =>
            {
                Dispatch(() =>
                {
                    job.ProgressPercentage = p;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                });
            });

            var result = await _engine.ConvertAsync(job, jobOptions, progress, jobCts.Token);

            Dispatch(() =>
            {
                if (result.Success)
                {
                    job.Status = ConversionStatus.Completed;
                    job.ProgressPercentage = 100;
                    job.OutputFilePath = result.OutputFilePath;
                    job.StatusText = "Completed";
                }
                else if (job.Status != ConversionStatus.Cancelled)
                {
                    job.Status = ConversionStatus.Failed;
                    job.ErrorMessage = result.ErrorMessage;
                    job.StatusText = "Failed";
                }

                JobCompleted?.Invoke(this, job);
                StateChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException)
        {
            Dispatch(() =>
            {
                job.Status = ConversionStatus.Cancelled;
                job.StatusText = "Cancelled";
                JobCompleted?.Invoke(this, job);
                StateChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            Dispatch(() =>
            {
                job.Status = ConversionStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.StatusText = "Error";
                JobCompleted?.Invoke(this, job);
                StateChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        finally
        {
            _semaphore.Release();
            _jobCts.TryRemove(job.Id, out _);
        }
    }

    /// <summary>
    /// Cancels a single conversion job if it is queued or running.
    /// </summary>
    public void CancelJob(Guid jobId)
    {
        if (_jobCts.TryGetValue(jobId, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }

        var job = Jobs.FirstOrDefault(j => j.Id == jobId);

        // Pending jobs have no CancellationTokenSource yet (it is created when the job starts), so
        // cancelling them means marking them Cancelled before the batch picks them up. Previously
        // only Queued jobs were handled, which made "cancel" a silent no-op for a fresh queue.
        if (job != null && job.Status is ConversionStatus.Queued or ConversionStatus.Pending)
        {
            job.Status = ConversionStatus.Cancelled;
            job.StatusText = "Cancelled";
        }
    }

    /// <summary>
    /// Cancels all active and queued conversions in the coordinator.
    /// </summary>
    public void CancelAll()
    {
        try
        {
            _batchCts?.Cancel();
        }
        catch (ObjectDisposedException) { }

        foreach (var cts in _jobCts.Values)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }
        }

        foreach (var job in Jobs.Where(j => j.Status == ConversionStatus.Queued || j.Status == ConversionStatus.Pending))
        {
            job.Status = ConversionStatus.Cancelled;
            job.StatusText = "Cancelled";
        }

        Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Removes completed, failed, and cancelled jobs from the list.
    /// </summary>
    public void ClearCompleted()
    {
        var finished = Jobs.Where(j => j.Status == ConversionStatus.Completed ||
                                       j.Status == ConversionStatus.Failed ||
                                       j.Status == ConversionStatus.Cancelled).ToList();

        foreach (var job in finished)
        {
            Dispatch(() => Jobs.Remove(job));
        }

        Dispatch(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void Dispatch(Action action)
    {
        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }
}
