using QuickConvert.Core.Models;
using Xunit;

namespace QuickConvert.Core.Tests;

public class ConversionJobTests
{
    [Fact]
    public void FromFilePath_PopulatesFileMetadata()
    {
        using var file = new TempFile(".PNG", sizeBytes: 4096);

        var job = ConversionJob.FromFilePath(file.Path);

        Assert.Equal(file.Path, job.InputFilePath);
        Assert.StartsWith("quickconvert-test-", job.FileName);
        Assert.Equal(4096, job.FileSizeBytes);
        Assert.Equal(".png", job.SourceExtension);      // normalised to lowercase, with the dot
        Assert.Equal(ConversionStatus.Pending, job.Status);
        Assert.Equal(string.Empty, job.TargetExtension);
    }

    [Fact]
    public void FromFilePath_HonoursDefaultTargetExtension()
    {
        using var file = new TempFile(".jpg");

        var job = ConversionJob.FromFilePath(file.Path, ".webp");

        Assert.Equal(".webp", job.TargetExtension);
    }

    [Fact]
    public void FromFilePath_MissingFile_ReportsZeroSizeInsteadOfThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"quickconvert-missing-{Guid.NewGuid():N}.png");

        var job = ConversionJob.FromFilePath(missing);

        Assert.Equal(0, job.FileSizeBytes);
        Assert.Equal("0 B", job.FormattedFileSize);
    }

    [Fact]
    public void FormattedFileSize_IsHumanReadable()
    {
        // Built with the ambient culture's number format so the assertion survives a de-DE machine.
        var expectedKb = 1.5m.ToString("n1") + " KB";

        using var file = new TempFile(".png", sizeBytes: 1536);
        var job = ConversionJob.FromFilePath(file.Path);

        Assert.Equal(expectedKb, job.FormattedFileSize);
        Assert.EndsWith(" KB", job.FormattedFileSize);
    }

    [Fact]
    public void FormattedFileSize_UpdatesWhenSizeChanges()
    {
        var job = new ConversionJob { FileSizeBytes = 0 };
        Assert.Equal("0 B", job.FormattedFileSize);

        job.FileSizeBytes = 5 * 1024 * 1024;   // NotifyPropertyChangedFor should push FormattedFileSize
        Assert.EndsWith(" MB", job.FormattedFileSize);
    }
}
