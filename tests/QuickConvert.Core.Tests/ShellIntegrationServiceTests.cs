using System.Runtime.InteropServices;
using System.Text.Json;
using QuickConvert.Core.Services;
using QuickConvert.Core.Shell;
using Xunit;

namespace QuickConvert.Core.Tests;

public class ShellIntegrationServiceTests
{
    private const string TestExePath = @"C:\Program Files\QuickConvert\QuickConvert.UI.exe";

    [Fact]
    public void Register_CreatesVerbsForEveryMatrixExtension()
    {
        var reg = new FakeRegistry();
        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");
        var service = new ShellIntegrationService(reg, manifestPath);

        var result = service.Register(TestExePath);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ConversionMatrix.Sources.Count, result.ExtensionCount);

        // Verify keys in fake registry
        foreach (var ext in ConversionMatrix.Sources)
        {
            var verbKeyPath = $@"SystemFileAssociations\{ext}\shell\{ShellIntegrationService.VerbName}";
            var verbKey = reg.ClassesRoot.OpenSubKey(verbKeyPath);
            Assert.NotNull(verbKey);
            Assert.Equal(ShellIntegrationService.VerbText, verbKey.GetValue(null));
            Assert.Equal(Path.GetFullPath(TestExePath), verbKey.GetValue("Icon"));
            Assert.Equal(ShellIntegrationService.MultiSelectPlayer, verbKey.GetValue("MultiSelectModel"));

            var cmdKey = verbKey.OpenSubKey("command");
            Assert.NotNull(cmdKey);
            Assert.Equal($"\"{Path.GetFullPath(TestExePath)}\" \"%1\"", cmdKey.GetValue(null));
        }

        // Verify manifest written
        Assert.True(File.Exists(manifestPath));
        var manifest = JsonSerializer.Deserialize<ShellRegistrationManifest>(File.ReadAllText(manifestPath));
        Assert.NotNull(manifest);
        Assert.Equal(ConversionMatrix.Sources.Count, manifest.RegisteredExtensions.Count);
        Assert.Equal(ShellIntegrationService.RegistrationVersion, manifest.Version);
    }

    [Fact]
    public void Register_IsIdempotent_DoubleRegisterSucceedsWithoutError()
    {
        var reg = new FakeRegistry();
        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");
        var service = new ShellIntegrationService(reg, manifestPath);

        var res1 = service.Register(TestExePath);
        var res2 = service.Register(TestExePath);

        Assert.True(res1.Success);
        Assert.True(res2.Success);
        Assert.True(service.IsRegistered());
    }

    [Fact]
    public void Unregister_RemovesAllKeysAndManifest()
    {
        var reg = new FakeRegistry();
        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");
        var service = new ShellIntegrationService(reg, manifestPath);

        service.Register(TestExePath);
        Assert.True(service.IsRegistered());

        var unregResult = service.Unregister();
        Assert.True(unregResult.Success, unregResult.Message);
        Assert.False(service.IsRegistered());
        Assert.False(File.Exists(manifestPath));

        foreach (var ext in ConversionMatrix.Sources)
        {
            var verbKeyPath = $@"SystemFileAssociations\{ext}\shell\{ShellIntegrationService.VerbName}";
            Assert.Null(reg.ClassesRoot.OpenSubKey(verbKeyPath));
        }
    }

    [Fact]
    public void Unregister_HandlesExtensionsRemovedSinceInstall()
    {
        var reg = new FakeRegistry();
        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");

        // Simulate an older installation where .legacy was registered
        var oldManifest = new ShellRegistrationManifest
        {
            Version = "0.9.0",
            ExecutablePath = TestExePath,
            RegisteredExtensions = new List<string> { ".jpg", ".legacy" }
        };
        Directory.CreateDirectory(tempDir.Path);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(oldManifest));

        // Create the keys in the registry
        reg.ClassesRoot.CreateSubKey($@"SystemFileAssociations\.jpg\shell\{ShellIntegrationService.VerbName}");
        reg.ClassesRoot.CreateSubKey($@"SystemFileAssociations\.legacy\shell\{ShellIntegrationService.VerbName}");

        var service = new ShellIntegrationService(reg, manifestPath);
        var result = service.Unregister();

        Assert.True(result.Success);
        Assert.Null(reg.ClassesRoot.OpenSubKey($@"SystemFileAssociations\.legacy\shell\{ShellIntegrationService.VerbName}"));
        Assert.Null(reg.ClassesRoot.OpenSubKey($@"SystemFileAssociations\.jpg\shell\{ShellIntegrationService.VerbName}"));
    }

    [Fact]
    public void Unregister_HandlesPartialRemovalCleanly()
    {
        var reg = new FakeRegistry();
        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");
        var service = new ShellIntegrationService(reg, manifestPath);

        service.Register(TestExePath);

        // Manually delete half the keys to simulate external modification
        reg.ClassesRoot.DeleteSubKeyTree($@"SystemFileAssociations\.jpg\shell\{ShellIntegrationService.VerbName}");

        var result = service.Unregister();
        Assert.True(result.Success);
        Assert.False(service.IsRegistered());
    }

    [Fact]
    public void Register_WhenRegistryRefusesWrite_ReturnsFailedResultWithoutThrowing()
    {
        var reg = new FakeRegistry(throwOnWrite: true);
        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");
        var service = new ShellIntegrationService(reg, manifestPath);

        var result = service.Register(TestExePath);

        Assert.False(result.Success);
        Assert.Contains("Access denied", result.Message);
    }

    [Fact]
    public void IsRegistered_ReflectsStateAccurately()
    {
        var reg = new FakeRegistry();
        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");
        var service = new ShellIntegrationService(reg, manifestPath);

        Assert.False(service.IsRegistered());

        service.Register(TestExePath);
        Assert.True(service.IsRegistered());

        service.Unregister();
        Assert.False(service.IsRegistered());
    }

    [Fact]
    public void RegisteredExtensions_EqualsConversionMatrixSources_AndRarIsSourceOnly()
    {
        var reg = new FakeRegistry();
        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");
        var service = new ShellIntegrationService(reg, manifestPath);

        var result = service.Register(TestExePath);
        Assert.True(result.Success);

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ShellRegistrationManifest>(manifestJson);
        Assert.NotNull(manifest);

        var registeredSet = new HashSet<string>(manifest.RegisteredExtensions, StringComparer.OrdinalIgnoreCase);
        var matrixSourcesSet = new HashSet<string>(ConversionMatrix.Sources, StringComparer.OrdinalIgnoreCase);

        // Assert the set of extensions the shell service registers equals ConversionMatrix.Sources exactly
        Assert.Equal(matrixSourcesSet, registeredSet);

        // Assert .rar is in that set while never appearing as a target
        Assert.Contains(".rar", registeredSet);
        Assert.Contains(".rar", matrixSourcesSet);

        foreach (var source in ConversionMatrix.Sources)
        {
            var targets = ConversionMatrix.GetTargets(source);
            Assert.DoesNotContain(".rar", targets, StringComparer.OrdinalIgnoreCase);
        }
    }

    [SkippableFact]
    public void Real_Registry_CanBeToggledOnWindows()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Real registry test requires Windows");

        using var tempDir = new TempTestDir();
        var manifestPath = Path.Combine(tempDir.Path, "shell-registration.json");
        var service = new ShellIntegrationService(new WindowsRegistry(), manifestPath);

        try
        {
            var regResult = service.Register(TestExePath);
            Assert.True(regResult.Success, regResult.Message);
            Assert.True(service.IsRegistered());
        }
        finally
        {
            var unregResult = service.Unregister();
            Assert.True(unregResult.Success, unregResult.Message);
            Assert.False(service.IsRegistered());
        }
    }

    private sealed class TempTestDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qc-shell-test-" + Guid.NewGuid().ToString("N"));
        public TempTestDir() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }

    private sealed class FakeRegistry : IRegistry
    {
        private readonly bool _throwOnWrite;
        public FakeRegistryKey ClassesRoot { get; }

        public FakeRegistry(bool throwOnWrite = false)
        {
            _throwOnWrite = throwOnWrite;
            ClassesRoot = new FakeRegistryKey(throwOnWrite);
        }

        public IRegistryKey? OpenCurrentUserClasses()
        {
            if (_throwOnWrite) throw new UnauthorizedAccessException("Access denied to registry.");
            return ClassesRoot;
        }
    }

    private sealed class FakeRegistryKey : IRegistryKey
    {
        private readonly bool _throwOnWrite;
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FakeRegistryKey> _subkeys = new(StringComparer.OrdinalIgnoreCase);

        public FakeRegistryKey(bool throwOnWrite = false)
        {
            _throwOnWrite = throwOnWrite;
        }

        public void SetValue(string? name, object value)
        {
            if (_throwOnWrite) throw new UnauthorizedAccessException("Access denied.");
            _values[name ?? string.Empty] = value;
        }

        public object? GetValue(string? name)
        {
            _values.TryGetValue(name ?? string.Empty, out var val);
            return val;
        }

        public IRegistryKey CreateSubKey(string subkey)
        {
            if (_throwOnWrite) throw new UnauthorizedAccessException("Access denied.");
            var parts = subkey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            FakeRegistryKey current = this;
            foreach (var part in parts)
            {
                if (!current._subkeys.TryGetValue(part, out var next))
                {
                    next = new FakeRegistryKey(_throwOnWrite);
                    current._subkeys[part] = next;
                }
                current = next;
            }
            return current;
        }

        public IRegistryKey? OpenSubKey(string subkey, bool writable = false)
        {
            var parts = subkey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            FakeRegistryKey current = this;
            foreach (var part in parts)
            {
                if (!current._subkeys.TryGetValue(part, out var next))
                {
                    return null;
                }
                current = next;
            }
            return current;
        }

        public void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = false)
        {
            if (_throwOnWrite) throw new UnauthorizedAccessException("Access denied.");
            var parts = subkey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            FakeRegistryKey current = this;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!current._subkeys.TryGetValue(parts[i], out var next))
                {
                    if (throwOnMissingSubKey) throw new ArgumentException("Subkey not found");
                    return;
                }
                current = next;
            }

            var last = parts[^1];
            if (!current._subkeys.Remove(last) && throwOnMissingSubKey)
            {
                throw new ArgumentException("Subkey not found");
            }
        }

        public string[] GetSubKeyNames() => _subkeys.Keys.ToArray();

        public void Dispose() { }
    }
}
