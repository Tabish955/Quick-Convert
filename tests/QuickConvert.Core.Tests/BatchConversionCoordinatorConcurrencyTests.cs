using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;
using Xunit;

namespace QuickConvert.Core.Tests;

/// <summary>
/// Covers the parallel-conversion limit. None of these tests depends on timing to PASS: they can only
/// fail if the limit is really wrong (or, for the "reaches N" test, after a 10 second safety timeout).
/// </summary>
public class BatchConversionCoordinatorConcurrencyTests
{
    private static ConversionOptions Options() => new() { TargetExtension = ".jpg", Quality = 90 };

    private static BatchConversionCoordinator CreateSut(IConversionEngine engine, int? degree) =>
        new(new ConversionEngineRouter([engine]), degree);

    private static List<TempFile> MakeFiles(int count) =>
        Enumerable.Range(0, count).Select(_ => new TempFile(".png")).ToList();

    private static void DisposeAll(List<TempFile> files) => files.ForEach(f => f.Dispose());

    [Fact]
    public void DefaultDegree_IsHalfTheProcessors_WithAFloorOfOne()
    {
        var sut = new BatchConversionCoordinator(new ConversionEngineRouter([new FakeConversionEngine()]));

        Assert.Equal(Math.Max(1, Environment.ProcessorCount / 2), sut.MaxDegreeOfParallelism);
    }

    [Fact]
    public void ExplicitDegree_IsUsedAsGiven()
    {
        var sut = CreateSut(new FakeConversionEngine(), degree: 3);

        Assert.Equal(3, sut.MaxDegreeOfParallelism);
    }

    [Fact]
    public void DegreeBelowOne_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSut(new FakeConversionEngine(), degree: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSut(new FakeConversionEngine(), degree: -2));
    }

    [Fact]
    public async Task DegreeOne_NeverRunsTwoConversionsAtTheSameTime()
    {
        var files = MakeFiles(5);
        try
        {
            var engine = new ConcurrencyProbeEngine(rendezvousCount: 1);
            var sut = CreateSut(engine, degree: 1);
            sut.EnqueueFiles(files.Select(f => f.Path));

            await sut.StartAllAsync(Options());

            Assert.Equal(1, engine.Peak);
            Assert.Equal(5, engine.Finished);
        }
        finally { DisposeAll(files); }
    }

    [Fact]
    public async Task DegreeTwo_RunsExactlyTwoAtOnce_AndNeverMore()
    {
        var files = MakeFiles(6);
        try
        {
            // The first job waits until a second one has started, which proves two really ran together.
            var engine = new ConcurrencyProbeEngine(rendezvousCount: 2);
            var sut = CreateSut(engine, degree: 2);
            sut.EnqueueFiles(files.Select(f => f.Path));

            await sut.StartAllAsync(Options());

            Assert.Equal(2, engine.Peak);
            Assert.Equal(6, engine.Finished);
            Assert.Equal(6, sut.CompletedJobsCount);
        }
        finally { DisposeAll(files); }
    }

    /// <summary>
    /// Counts how many conversions are in flight. With rendezvousCount N, every job blocks until N jobs
    /// are in flight at once (10 s safety timeout), then holds its slot briefly so an illegal extra
    /// start would be counted.
    /// </summary>
    private sealed class ConcurrencyProbeEngine(int rendezvousCount) : IConversionEngine
    {
        private readonly TaskCompletionSource _rendezvous = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _current;
        private int _peak;
        private int _finished;

        public int Peak => Volatile.Read(ref _peak);
        public int Finished => Volatile.Read(ref _finished);

        public string EngineName => "Concurrency probe";
        public IReadOnlyList<string> SupportedSourceExtensions { get; } = [".png"];

        public bool CanConvert(string sourceExt, string targetExt) =>
            FormatRegistry.NormalizeExtension(sourceExt) == ".png" &&
            FormatRegistry.NormalizeExtension(targetExt) == ".jpg";

        public async Task<ConversionResult> ConvertAsync(
            ConversionJob job, ConversionOptions options, IProgress<double>? progress, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _current);
            RaisePeak(now);
            if (now >= rendezvousCount) _rendezvous.TrySetResult();

            try
            {
                await _rendezvous.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                await Task.Delay(25, ct);
                Interlocked.Increment(ref _finished);
                return new ConversionResult(true, Path.ChangeExtension(job.InputFilePath, ".jpg"));
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        private void RaisePeak(int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref _peak)))
            {
                if (Interlocked.CompareExchange(ref _peak, value, seen) == seen) return;
            }
        }
    }
}
