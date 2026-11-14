using QuickConvert.Engines.Docs.Tools;

namespace QuickConvert.Engines.Docs.Tests;

public class ToolLocatorTests
{
    private static ToolLocator Make(HashSet<string> existing, string? path = null) =>
        new("pandoc", "pandoc.exe", "3.5", existing.Contains, @"C:\Users\u\AppData\Local", @"C:\app\", path);

    [Fact]
    public void LocalAppData_versioned_folder_wins()
    {
        var a = Path.Combine(@"C:\Users\u\AppData\Local", "QuickConvert", "tools", "pandoc", "3.5", "pandoc.exe");
        var b = Path.Combine(@"C:\app\", "tools", "pandoc", "pandoc.exe");
        Assert.Equal(a, Make(new() { a, b }).Locate());
    }

    [Fact]
    public void App_directory_is_second()
    {
        var b = Path.Combine(@"C:\app\", "tools", "pandoc", "pandoc.exe");
        Assert.Equal(b, Make(new() { b }).Locate());
    }

    [Fact]
    public void PATH_is_last_and_missing_returns_null()
    {
        var onPath = Path.Combine(@"C:\bin", "pandoc.exe");
        Assert.Equal(onPath, Make(new() { onPath }, @"C:\x;C:\bin").Locate());
        Assert.Null(Make(new(), @"C:\x;C:\bin").Locate());
    }
}
