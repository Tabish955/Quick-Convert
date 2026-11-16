namespace QuickConvert.Core.Tools;

/// <summary>
/// Service that probes the local system for external conversion tool binaries.
/// </summary>
public interface IToolProbeService
{
    /// <summary>
    /// Executes the tool probe asynchronously without blocking the calling thread.
    /// </summary>
    Task<ToolProbeResult> ProbeAsync();

    /// <summary>
    /// Synchronously probes the local system for external tools.
    /// </summary>
    ToolProbeResult Probe();
}
