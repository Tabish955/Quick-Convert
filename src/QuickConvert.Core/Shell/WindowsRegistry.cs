using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace QuickConvert.Core.Shell;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    public IRegistryKey? OpenCurrentUserClasses()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true);
        return key != null ? new WindowsRegistryKey(key) : null;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsRegistryKey : IRegistryKey
{
    private readonly RegistryKey _key;

    public WindowsRegistryKey(RegistryKey key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public void SetValue(string? name, object value) => _key.SetValue(name, value);

    public object? GetValue(string? name) => _key.GetValue(name);

    public IRegistryKey CreateSubKey(string subkey)
    {
        var sub = _key.CreateSubKey(subkey, writable: true);
        return new WindowsRegistryKey(sub);
    }

    public IRegistryKey? OpenSubKey(string subkey, bool writable = false)
    {
        var sub = _key.OpenSubKey(subkey, writable);
        return sub != null ? new WindowsRegistryKey(sub) : null;
    }

    public void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = false)
    {
        _key.DeleteSubKeyTree(subkey, throwOnMissingSubKey);
    }

    public string[] GetSubKeyNames() => _key.GetSubKeyNames();

    public void Dispose() => _key.Dispose();
}
