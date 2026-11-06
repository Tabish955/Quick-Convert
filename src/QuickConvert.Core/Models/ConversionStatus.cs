namespace QuickConvert.Core.Models;

/// <summary>
/// Status lifecycle of a conversion job.
/// </summary>
public enum ConversionStatus
{
    Pending,
    Queued,
    Converting,
    Completed,
    Failed,
    Cancelled
}
