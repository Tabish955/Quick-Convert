namespace QuickConvert.Core.Shell;

/// <summary>
/// Abstraction over Windows Registry keys to enable 100% in-memory unit testing
/// without modifying real Windows registry.
/// </summary>
public interface IRegistryKey : IDisposable
{
    void SetValue(string? name, object value);
    object? GetValue(string? name);
    IRegistryKey CreateSubKey(string subkey);
    IRegistryKey? OpenSubKey(string subkey, bool writable = false);
    void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = false);
    string[] GetSubKeyNames();
}

/// <summary>
/// Registry root access abstraction.
/// </summary>
public interface IRegistry
{
    /// <summary>
    /// Opens or creates the Software\Classes subkey under HKEY_CURRENT_USER.
    /// </summary>
    IRegistryKey? OpenCurrentUserClasses();
}
