namespace QuickConvert.Core.Tests;

/// <summary>Creates a real file with a given extension in the temp folder and deletes it on dispose.</summary>
internal sealed class TempFile : IDisposable
{
    public string Path { get; }

    public TempFile(string extension = ".png", int sizeBytes = 64)
    {
        if (!extension.StartsWith('.')) extension = "." + extension;

        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"quickconvert-test-{Guid.NewGuid():N}{extension}");

        File.WriteAllBytes(Path, new byte[Math.Max(sizeBytes, 0)]);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch (IOException)
        {
            // A failing cleanup must not fail the test.
        }
    }
}
