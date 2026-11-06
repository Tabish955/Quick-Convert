using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QuickConvert.Core.Models;
using QuickConvert.Core.Services;

namespace QuickConvert.UI.ViewModels;

/// <summary>
/// ViewModel driving the Main Full Dashboard.
/// Manages batch queue, concurrency, drag-and-drop file ingestion, and batch settings.
/// </summary>
public partial class MainDashboardViewModel : ObservableObject
{
    private readonly BatchConversionCoordinator _coordinator;

    public ObservableCollection<ConversionJob> Jobs => _coordinator.Jobs;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private int _activeJobsCount;

    [ObservableProperty]
    private int _completedJobsCount;

    [ObservableProperty]
    private int _totalJobsCount;

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private string _outputFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private int _quality = 85;

    [ObservableProperty]
    private bool _overwriteExisting = false;

    [ObservableProperty]
    private string _selectedGlobalFormat = string.Empty;

    public ObservableCollection<string> AvailableGlobalFormats { get; } = new();

    public MainDashboardViewModel(BatchConversionCoordinator coordinator)
    {
        _coordinator = coordinator;

        // Populate sample popular targets for global batch selector
        foreach (var fmt in new[] { ".png", ".jpg", ".webp", ".pdf", ".mp4", ".mp3", ".docx", ".zip", ".xlsx" })
        {
            AvailableGlobalFormats.Add(fmt);
        }

        _coordinator.StateChanged += OnCoordinatorStateChanged;
        _coordinator.BatchCompleted += OnBatchCompleted;

        // Default output path to user's Downloads or Desktop
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        OutputFolderPath = Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        UpdateState();
    }

    private void OnCoordinatorStateChanged(object? sender, EventArgs e)
    {
        UpdateState();
    }

    private void OnBatchCompleted(object? sender, EventArgs e)
    {
        UpdateState();
    }

    private void UpdateState()
    {
        OverallProgress = _coordinator.OverallProgress;
        ActiveJobsCount = _coordinator.ActiveJobsCount;
        CompletedJobsCount = _coordinator.CompletedJobsCount;
        TotalJobsCount = _coordinator.TotalJobsCount;
        IsConverting = _coordinator.IsConverting;
    }

    partial void OnSelectedGlobalFormatChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        // Apply selected format to all pending jobs in the queue
        foreach (var job in Jobs.Where(j => j.Status == ConversionStatus.Pending))
        {
            job.TargetExtension = value;
        }
    }

    /// <summary>
    /// Ingests multiple files into the conversion queue (e.g. from Explorer or Drag-and-Drop).
    /// </summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        _coordinator.EnqueueFiles(paths, string.IsNullOrWhiteSpace(SelectedGlobalFormat) ? null : SelectedGlobalFormat);
        UpdateState();
    }

    /// <summary>
    /// Opens file dialog to allow user to add files to the batch queue.
    /// </summary>
    [RelayCommand]
    public void AddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Files to Convert with QuickConvert",
            Multiselect = true,
            Filter = "All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            AddFiles(dialog.FileNames);
        }
    }

    /// <summary>
    /// Starts batch conversion of all pending jobs.
    /// </summary>
    [RelayCommand]
    public async Task StartQueueAsync()
    {
        if (Jobs.Count == 0 || IsConverting) return;

        var options = new ConversionOptions
        {
            TargetExtension = SelectedGlobalFormat,
            OutputDirectory = OutputFolderPath,
            Quality = Quality,
            OverwriteExisting = OverwriteExisting
        };

        await _coordinator.StartAllAsync(options);
    }

    /// <summary>
    /// Cancels all active conversion jobs.
    /// </summary>
    [RelayCommand]
    public void CancelAll()
    {
        _coordinator.CancelAll();
        UpdateState();
    }

    /// <summary>
    /// Cancels an individual job.
    /// </summary>
    [RelayCommand]
    public void CancelJob(ConversionJob? job)
    {
        if (job != null)
        {
            _coordinator.CancelJob(job.Id);
            UpdateState();
        }
    }

    /// <summary>
    /// Removes an individual job from the queue.
    /// </summary>
    [RelayCommand]
    public void RemoveJob(ConversionJob? job)
    {
        if (job != null && job.Status != ConversionStatus.Converting)
        {
            Jobs.Remove(job);
            UpdateState();
        }
    }

    /// <summary>
    /// Clears completed, failed, and cancelled jobs from the list.
    /// </summary>
    [RelayCommand]
    public void ClearCompleted()
    {
        _coordinator.ClearCompleted();
        UpdateState();
    }

    /// <summary>
    /// Opens the current output folder in Windows File Explorer.
    /// </summary>
    [RelayCommand]
    public void OpenOutputFolder()
    {
        try
        {
            if (Directory.Exists(OutputFolderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = OutputFolderPath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    /// <summary>
    /// Browse for custom output directory.
    /// </summary>
    [RelayCommand]
    public void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder for Converted Files",
            InitialDirectory = Directory.Exists(OutputFolderPath) ? OutputFolderPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog() == true)
        {
            OutputFolderPath = dialog.FolderName;
        }
    }
}
