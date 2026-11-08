using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;

namespace QuickConvert.Core.Services;

/// <summary>
/// A file that was refused before reaching the queue, with a user-facing reason.
/// </summary>
public record RejectedFile(string FilePath, string Reason);

/// <summary>Outcome of an enqueue call: jobs accepted into the queue, and files refused up front.</summary>
public record EnqueueResult(IReadOnlyList<ConversionJob> Added, IReadOnlyList<RejectedFile> Rejected);

/// <summary>
/// Coordinates batch conversion operations, manages concurrency via SemaphoreSlim,
/// tracks job states, and handles cancellation. All conversions are resolved through
/// the IConversionEngineRouter; engines are never invoked directly from here.
/// </summary>
public class BatchConversionCoordinator
{
    private readonly IConversionEngineRouter _router;
    private readonly SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _jobCts = new();
    private CancellationTokenSource? _batchCts;
    private readonly SynchronizationContext? _syncContext;

    private readonly int _concurrencyLimit;
    private bool _isConverting;

    /// <summary>
    /// How many conversions may run at the same time. Defaults to half the logical processors
    /// (at least 1) unless a value was passed to the constructor.
    /// </summary>
    public int MaxDegreeOfParallelism => _concurrencyLimit;

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
    public event EventHandler<RejectedFile>? JobRejected;
    public event EventHandler? BatchCompleted;

    /// <param name="router">Resolves which engine performs each conversion.</param>
    /// <param name="maxDegreeOfParallelism">
    /// Optional cap on simultaneous conversions (must be at least 1). Leave it out for the default of
    /// half the logical processors. Making it injectable lets tests assert the configured degree
    /// without relying on timing.
    /// </param>
    public BatchConversionCoordinator(IConversionEngineRouter router, int? maxDegreeOfParallelism = null)
    {
        if (maxDegreeOfParallelism is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDegreeOfParallelism), maxDegreeOfParallelism, "At least one conversion must be allowed to run.");
        }

        _router = router;
        _concurrencyLimit = maxDegreeOfParallelism ?? Math.Max(1, Environment.ProcessorCount / 2);
        _semaphore = new SemaphoreSlim(_concurrencyLimit, _concurrencyLimit);
        _syncContext = SynchronizationContext.Current;
    }

    /// <summary>
    /// Enqueues multiple file paths into the batch queue.
    /// Infers file category, size, and sets recommended target format if not supplied.
    /// Files whose conversion cannot be performed are refused BEFORE they reach the queue,
    /// with a user-facing reason, instead of failing deep inside the pipeline.
    /// </summary>
    public EnqueueResult EnqueueFiles(IEnumerable<string> filePaths, string? defaultTargetExt = null)
    {
        var addedJobs = new List<ConversionJob>();
        var rejected = new List<RejectedFile>();

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
                var available = _router.GetAvailableTargets(job.SourceExtension);
                if (available.Count == 0)
                {
                    var r = new RejectedFile(path, $"{job.SourceExtension} is not a supported conversion source.");
                    rejected.Add(r);
                    Dispatch(() => JobRejected?.Invoke(this, r));
                    continue;
                }
                job.TargetExtension = available[0];
            }

            // Capability gate: the pair must be convertible before it reaches the queue.
            if (_router.Resolve(job.SourceExtension, job.TargetExtension) is null)
            {
                var available = _router.GetAvailableTargets(job.SourceExtension);
                var reason = available.Count == 0
                    ? $"{job.SourceExtension} → {job.TargetExtension} is not supported."
                    : $"{job.SourceExtension} → {job.TargetExtension} is not supported. Available targets: {string.Join(", ", available)}.";
                var r = new RejectedFile(path, reason);
                rejected.Add(r);
                Dispatch(() => JobRejected?.Invoke(this, r));
                continue;
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
        return new EnqueueResult(addedJobs, rejected);
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

            // The enqueue-time gate makes this unreachable in practice; it stays as a
            // fail-fast guard so a job can never reach an engine the router cannot serve.
            var effectiveTarget = options != null && !string.IsNullOrWhiteSpace(options.TargetExtension)
                ? options.TargetExtension
                : job.TargetExtension;

            var engine = _router.Resolve(job.SourceExtension, effectiveTarget);
            if (engine is null)
            {
                job.Status = ConversionStatus.Failed;
                job.ErrorMessage = $"{job.SourceExtension} → {effectiveTarget} is not supported.";
                job.StatusText = "Failed";
                Dispatch(() =>
                {
                    JobCompleted?.Invoke(this, job);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                });
                return;
            }

            var jobOptions = options ?? new ConversionOptions
            {
                TargetExtension = effectiveTarget,
                Quality = 85
            };

            job.TargetExtension = effectiveTarget;

            // Ensure job's specific target format is respected if options has blank target
            if (string.IsNullOrWhiteSpace(jobOptions.TargetExtension) && !string.IsNullOrWhiteSpace(job.TargetExtension))
            {
                jobOptions = jobOptions with { TargetExtension = effectiveTarget };

            }

            var progress = new Progress<double>(p =>
            {
                Dispatch(() =>
                {
                    if (job.Status == ConversionStatus.Converting)
                    {
                        job.ProgressPercentage = p;
                        StateChanged?.Invoke(this, EventArgs.Empty);
                    }
                });
            });

            var result = await engine.ConvertAsync(job, jobOptions, progress, jobCts.Token);

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
