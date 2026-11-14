using QuickConvert.Core.Models;
using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs;

internal readonly record struct JobView(string SourcePath, string TargetExtension, string OutputDirectory, bool Overwrite);

internal static class EngineBridge
{
    public static JobView Read(ConversionJob job, ConversionOptions options)
    {
        var outDir = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? (Directory.Exists(job.InputFilePath) ? Path.GetDirectoryName(job.InputFilePath.TrimEnd('\\', '/'))! : Path.GetDirectoryName(job.InputFilePath)!)
            : options.OutputDirectory!;
        return new JobView(job.InputFilePath, job.TargetExtension, outDir, options.OverwriteExisting);
    }

    public static ConversionResult ToResult(ToolResult r, TimeSpan duration = default) =>
        r.Success
            ? new ConversionResult(true, r.OutputPath ?? string.Empty, null, duration)
            : new ConversionResult(false, string.Empty, r.Message, duration);

    public static IProgress<int>? Progress(IProgress<double>? p) =>
        p is null ? null : new Progress<int>(pct => p.Report(pct));
}
