namespace QuickConvert.Core.Shell;

public readonly record struct ShellOperationResult(bool Success, string Message, int ExtensionCount)
{
    public static ShellOperationResult Ok(string message, int count) => new(true, message, count);
    public static ShellOperationResult Fail(string message) => new(false, message, 0);
}

public interface IShellIntegration
{
    /// <summary>
    /// Registers Explorer context menu verbs for all matrix extensions under HKCU.
    /// Idempotent; writes shell-registration.json tracking registered extensions.
    /// </summary>
    ShellOperationResult Register(string executablePath);

    /// <summary>
    /// Removes Explorer context menu verbs based on the stored registration manifest
    /// (falling back to the current matrix if manifest is absent). Residue-free.
    /// </summary>
    ShellOperationResult Unregister();

    /// <summary>
    /// Checks whether the shell verb is currently registered.
    /// </summary>
    bool IsRegistered();

    /// <summary>
    /// Gets the list of currently registered extensions from the manifest.
    /// </summary>
    IReadOnlyList<string> GetRegisteredExtensions();
}
