using CommunityToolkit.Mvvm.ComponentModel;

namespace QuickConvert.Core.Models;

/// <summary>
/// Observable model representing an individual file conversion task in the pipeline.
/// </summary>
public partial class ConversionJob : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _inputFilePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedFileSize))]
    private long _fileSizeBytes;

    [ObservableProperty]
    private string _sourceExtension = string.Empty;

    [ObservableProperty]
    private string _targetExtension = string.Empty;

    [ObservableProperty]
    private FileCategory _category = FileCategory.Unknown;

    [ObservableProperty]
    private ConversionStatus _status = ConversionStatus.Pending;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string? _outputFilePath;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private JobEndpointKind _sourceKind = JobEndpointKind.File;

    [ObservableProperty]
    private JobEndpointKind _targetKind = JobEndpointKind.File;

    /// <summary>
    /// Human-readable file size (e.g. "12.4 MB").
    /// </summary>
    public string FormattedFileSize => FormatBytes(FileSizeBytes);

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

    /// <summary>
    /// Factory helper to build a ConversionJob from a file path.
    /// </summary>
    public static ConversionJob FromFilePath(string path, string? defaultTargetExt = null)
    {
        var fileInfo = new FileInfo(path);
        var ext = fileInfo.Extension.ToLowerInvariant();
        return new ConversionJob
        {
            InputFilePath = fileInfo.FullName,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            SourceExtension = ext,
            TargetExtension = defaultTargetExt ?? string.Empty,
            SourceKind = JobEndpointKind.File,
            TargetKind = JobEndpointKind.File,
            Status = ConversionStatus.Pending,
            StatusText = "Pending"
        };
    }

    /// <summary>
    /// Factory helper to build a ConversionJob from a file or directory path.
    /// </summary>
    public static ConversionJob FromPath(string path, string? defaultTargetExt = null)
    {
        if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            return new ConversionJob
            {
                InputFilePath = dirInfo.FullName,
                FileName = dirInfo.Name,
                FileSizeBytes = 0,
                SourceExtension = string.Empty,
                TargetExtension = defaultTargetExt ?? string.Empty,
                SourceKind = JobEndpointKind.Directory,
                TargetKind = JobEndpointKind.File,
                Status = ConversionStatus.Pending,
                StatusText = "Pending"
            };
        }

        return FromFilePath(path, defaultTargetExt);
    }
}
