using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickConvert.Core.Interfaces;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;

namespace QuickConvert.UI.ViewModels;

/// <summary>
/// ViewModel driving the lightweight Convertio-style Quick Popup.
/// Displays when user launches Quick Convert on files from Windows File Explorer.
///
/// Phase 2: all conversions go through the shared BatchConversionCoordinator (the same
/// singleton the dashboard uses), so popup jobs run with full concurrency, share
/// cancellation, and appear on the dashboard with the same job ids. The ViewModel never
/// touches an IConversionEngine directly; the router gates what may even be offered.
/// </summary>
public partial class QuickPopupViewModel : ObservableObject
{
    private readonly BatchConversionCoordinator _coordinator;
    private readonly IConversionEngineRouter _router;

    [ObservableProperty]
    private string _fileSummary = "No files selected";

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private FileCategory _detectedCategory = FileCategory.Unknown;

    [ObservableProperty]
    private string _selectedTargetFormat = string.Empty;

    [ObservableProperty]
    private int _quality = 85;

    [ObservableProperty]
    private bool _overwriteExisting = false;

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _statusMessage = "Select target format to convert";

    [ObservableProperty]
    private bool _conversionCompleted;

    [ObservableProperty]
    private string? _lastOutputFilePath;

    /// <summary>True when the selection has no convertible target (e.g. 5 JPGs + 1 ZIP). UI shows "Open Dashboard".</summary>
    [ObservableProperty]
    private bool _noCommonTarget;

    public ObservableCollection<string> AvailableTargetFormats { get; } = new();
    public ObservableCollection<string> SelectedFiles { get; } = new();

    // Navigation and window actions
    public event Action? RequestClose;
    public event Action<IReadOnlyList<string>>? RequestOpenDashboard;

    public QuickPopupViewModel(BatchConversionCoordinator coordinator, IConversionEngineRouter router)
    {
        _coordinator = coordinator;
        _router = router;
    }

    /// <summary>
    /// Loads incoming file paths from Windows File Explorer into the popup.
    /// Analyzes extensions, finds common targets via the router, and sets initial defaults.
    /// Mixed selections with an empty intersection show an explicit message — never the
    /// first file's target list.
    /// </summary>
    public void LoadFiles(IReadOnlyList<string> paths)
    {
        SelectedFiles.Clear();
        AvailableTargetFormats.Clear();
        ConversionCompleted = false;
        NoCommonTarget = false;
        OverallProgress = 0;
        StatusMessage = "Ready";

        var validPaths = paths.Where(File.Exists).ToList();
        if (validPaths.Count == 0)
        {
            FileSummary = "No valid files selected.";
            FileCount = 0;
            DetectedCategory = FileCategory.Unknown;
            return;
        }

        foreach (var path in validPaths)
        {
            SelectedFiles.Add(path);
        }

        FileCount = validPaths.Count;

        if (FileCount == 1)
        {
            var singleFile = validPaths[0];
            var ext = Path.GetExtension(singleFile);
            var fi = new FileInfo(singleFile);
            DetectedCategory = FormatRegistry.GetCategory(ext);
            FileSummary = $"{fi.Name} ({FormatBytes(fi.Length)})";

            var targets = _router.GetAvailableTargets(ext);
            foreach (var t in targets)
            {
                AvailableTargetFormats.Add(t);
            }
        }
        else
        {
            var firstExt = Path.GetExtension(validPaths[0]);
            DetectedCategory = FormatRegistry.GetCategory(firstExt);
            FileSummary = $"{FileCount} files selected ({DetectedCategory})";

            // Common targets across ALL files, via the router: matrix ∩ engine capability.
            var common = _router.GetAvailableTargets(firstExt);
            foreach (var path in validPaths.Skip(1))
            {
                var next = _router.GetAvailableTargets(Path.GetExtension(path));
                common = common.Intersect(next, StringComparer.OrdinalIgnoreCase).ToList();
                if (common.Count == 0) break;
            }

            foreach (var t in common)
            {
                AvailableTargetFormats.Add(t);
            }
        }

        NoCommonTarget = AvailableTargetFormats.Count == 0;
        SelectedTargetFormat = AvailableTargetFormats.Count > 0 ? AvailableTargetFormats[0] : string.Empty;

        StatusMessage = NoCommonTarget
            ? "No common target format for this selection."
            : $"Convert {FileCount} {(FileCount == 1 ? "file" : "files")} to {SelectedTargetFormat.ToUpperInvariant()}";
    }

    partial void OnSelectedTargetFormatChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsConverting && !ConversionCompleted)
        {
            StatusMessage = $"Convert {FileCount} {(FileCount == 1 ? "file" : "files")} to {value.ToUpperInvariant()}";
        }
    }

    /// <summary>
    /// Enqueues all selected files on the shared coordinator and runs the batch.
    /// Impossible conversions were already un-pickable; anything still rejected up front
    /// (e.g. engine gap) is surfaced with its reason instead of failing silently.
    /// </summary>
    [RelayCommand]
    public async Task ConvertNowAsync()
    {
        if (SelectedFiles.Count == 0 || string.IsNullOrWhiteSpace(SelectedTargetFormat) || IsConverting)
            return;

        IsConverting = true;
        ConversionCompleted = false;
        OverallProgress = 0;
        StatusMessage = "Starting conversion...";

        var options = new ConversionOptions
        {
            TargetExtension = SelectedTargetFormat,
            Quality = Quality,
            OverwriteExisting = OverwriteExisting
        };

        var result = _coordinator.EnqueueFiles(SelectedFiles, SelectedTargetFormat);

        if (result.Rejected.Count > 0)
        {
            StatusMessage = string.Join("  |  ", result.Rejected.Select(r => r.Reason));
        }

        if (result.Added.Count == 0)
        {
            IsConverting = false;
            return;
        }

        var total = result.Added.Count;
        var completed = 0;
        var failed = 0;

        void OnJobCompleted(object? sender, ConversionJob job)
        {
            if (job.Status == ConversionStatus.Completed)
            {
                completed++;
                LastOutputFilePath = job.OutputFilePath;
            }
            else if (job.Status == ConversionStatus.Failed)
            {
                failed++;
            }

            OverallProgress = _coordinator.OverallProgress;
            StatusMessage = $"Converting... {completed + failed} of {total} done";
        }

        _coordinator.JobCompleted += OnJobCompleted;
        try
        {
            await _coordinator.StartAllAsync(options);

            OverallProgress = 100;
            ConversionCompleted = failed == 0;
            StatusMessage = failed == 0
                ? $"Converted {completed} of {total} successfully!"
                : $"Finished: {completed} succeeded, {failed} failed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            _coordinator.JobCompleted -= OnJobCompleted;
            IsConverting = false;
        }
    }

    /// <summary>
    /// Cancels the active batch through the coordinator (shared with the dashboard).
    /// </summary>
    [RelayCommand]
    public void CancelConversion()
    {
        _coordinator.CancelAll();
        IsConverting = false;
        StatusMessage = "Cancelled";
    }

    /// <summary>
    /// Opens the full batch conversion dashboard with current files enqueued.
    /// </summary>
    [RelayCommand]
    public void OpenFullDashboard()
    {
        RequestOpenDashboard?.Invoke(SelectedFiles.ToList());
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Opens the destination folder in Windows File Explorer.
    /// </summary>
    [RelayCommand]
    public void OpenOutputFolder()
    {
        try
        {
            var folder = !string.IsNullOrWhiteSpace(LastOutputFilePath) && File.Exists(LastOutputFilePath)
                ? Path.GetDirectoryName(LastOutputFilePath)
                : (SelectedFiles.Count > 0 ? Path.GetDirectoryName(SelectedFiles[0]) : null);

            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024m) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024m;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
