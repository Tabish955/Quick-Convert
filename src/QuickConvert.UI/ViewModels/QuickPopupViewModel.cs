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
/// </summary>
public partial class QuickPopupViewModel : ObservableObject
{
    private readonly IConversionEngine _engine;
    private readonly BatchConversionCoordinator _coordinator;
    private CancellationTokenSource? _conversionCts;

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

    public ObservableCollection<string> AvailableTargetFormats { get; } = new();
    public ObservableCollection<string> SelectedFiles { get; } = new();

    // Navigation and window actions
    public event Action? RequestClose;
    public event Action<IReadOnlyList<string>>? RequestOpenDashboard;

    public QuickPopupViewModel(IConversionEngine engine, BatchConversionCoordinator coordinator)
    {
        _engine = engine;
        _coordinator = coordinator;
    }

    /// <summary>
    /// Loads incoming file paths from Windows File Explorer into the popup.
    /// Analyzes extensions, finds common targets, and sets initial defaults.
    /// </summary>
    public void LoadFiles(IReadOnlyList<string> paths)
    {
        SelectedFiles.Clear();
        AvailableTargetFormats.Clear();
        ConversionCompleted = false;
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

            var targets = FormatRegistry.GetSupportedTargetFormats(ext);
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

            var common = FormatRegistry.GetCommonTargetFormats(validPaths);
            if (common.Count > 0)
            {
                foreach (var t in common)
                {
                    AvailableTargetFormats.Add(t);
                }
            }
            else
            {
                // Fallback to category formats
                var targets = FormatRegistry.GetSupportedTargetFormats(firstExt);
                foreach (var t in targets)
                {
                    AvailableTargetFormats.Add(t);
                }
            }
        }

        SelectedTargetFormat = AvailableTargetFormats.Count > 0 ? AvailableTargetFormats[0] : string.Empty;
        StatusMessage = AvailableTargetFormats.Count > 0
            ? $"Convert {FileCount} {(FileCount == 1 ? "file" : "files")} to {SelectedTargetFormat.ToUpperInvariant()}"
            : "No compatible output format found";
    }

    partial void OnSelectedTargetFormatChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsConverting && !ConversionCompleted)
        {
            StatusMessage = $"Convert {FileCount} {(FileCount == 1 ? "file" : "files")} to {value.ToUpperInvariant()}";
        }
    }

    /// <summary>
    /// Executes conversion for all selected files with progress reporting.
    /// </summary>
    [RelayCommand]
    public async Task ConvertNowAsync()
    {
        if (SelectedFiles.Count == 0 || string.IsNullOrWhiteSpace(SelectedTargetFormat))
            return;

        IsConverting = true;
        ConversionCompleted = false;
        OverallProgress = 0;
        StatusMessage = "Starting conversion...";

        _conversionCts?.Dispose();
        _conversionCts = new CancellationTokenSource();

        var options = new ConversionOptions
        {
            TargetExtension = SelectedTargetFormat,
            Quality = Quality,
            OverwriteExisting = OverwriteExisting
        };

        try
        {
            int total = SelectedFiles.Count;
            int completedCount = 0;
            string? lastOutput = null;

            for (int i = 0; i < total; i++)
            {
                _conversionCts.Token.ThrowIfCancellationRequested();

                var filePath = SelectedFiles[i];
                var job = ConversionJob.FromFilePath(filePath, SelectedTargetFormat);
                job.Category = DetectedCategory;

                var progress = new Progress<double>(p =>
                {
                    double fileShare = 100.0 / total;
                    double currentTotal = (completedCount * fileShare) + (p * fileShare / 100.0);
                    OverallProgress = Math.Round(currentTotal, 1);
                    StatusMessage = $"Converting file {i + 1} of {total} ({OverallProgress:0}%)...";
                });

                var result = await _engine.ConvertAsync(job, options, progress, _conversionCts.Token);
                if (result.Success)
                {
                    completedCount++;
                    lastOutput = result.OutputFilePath;
                }
            }

            LastOutputFilePath = lastOutput;
            OverallProgress = 100;
            ConversionCompleted = true;
            StatusMessage = $"Converted {completedCount} of {total} successfully!";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Conversion was cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConverting = false;
        }
    }

    /// <summary>
    /// Cancels active conversion.
    /// </summary>
    [RelayCommand]
    public void CancelConversion()
    {
        _conversionCts?.Cancel();
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
