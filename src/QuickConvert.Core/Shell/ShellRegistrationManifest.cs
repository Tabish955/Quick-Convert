namespace QuickConvert.Core.Shell;

/// <summary>
/// Persisted registration manifest stored at %LOCALAPPDATA%\QuickConvert\shell-registration.json.
/// Guarantees that uninstallation removes the exact list of extensions that was registered,
/// even if the matrix format list changes across product versions.
/// </summary>
public sealed class ShellRegistrationManifest
{
    public string Version { get; set; } = "1.0.0";
    public string ExecutablePath { get; set; } = string.Empty;
    public List<string> RegisteredExtensions { get; set; } = new();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
