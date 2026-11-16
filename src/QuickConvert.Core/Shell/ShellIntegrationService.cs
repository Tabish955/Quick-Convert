using System.Runtime.Versioning;
using System.Text.Json;
using QuickConvert.Core.Diagnostics;
using QuickConvert.Core.Services;

namespace QuickConvert.Core.Shell;

/// <summary>
/// Manages per-user Windows Explorer context menu verbs under HKCU\Software\Classes\SystemFileAssociations.
/// Pure HKCU operation: zero administrator elevation required.
/// Stores a JSON manifest to guarantee clean, residue-free uninstallation across matrix changes.
/// </summary>
public sealed class ShellIntegrationService : IShellIntegration
{
    public const string VerbName = "QuickConvert";
    public const string VerbText = "Convert with QuickConvert";
    public const string MultiSelectPlayer = "Player";
    public const string RegistrationVersion = "1.0.0";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly IRegistry _registry;
    private readonly string _manifestPath;
    private readonly IReadOnlyList<string> _sourceExtensions;

    public ShellIntegrationService(
        IRegistry? registry = null,
        string? manifestPath = null,
        IReadOnlyList<string>? sourceExtensions = null)
    {
        _registry = registry ?? CreateDefaultRegistry();
        _manifestPath = manifestPath ?? DefaultManifestPath;
        _sourceExtensions = sourceExtensions ?? ConversionMatrix.Sources;
    }

    private static IRegistry CreateDefaultRegistry()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRegistry();
        }
        return new UnsupportedPlatformRegistry();
    }

    public static string DefaultManifestPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickConvert",
        "shell-registration.json");

    public ShellOperationResult Register(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return ShellOperationResult.Fail("Executable path cannot be empty.");
        }

        var fullExePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExePath))
        {
            QuickConvertLogger.Warn($"Registering shell verb for executable that does not yet exist on disk: {fullExePath}");
        }

        try
        {
            using var classesKey = _registry.OpenCurrentUserClasses();
            if (classesKey == null)
            {
                return ShellOperationResult.Fail("Unable to access HKCU\\Software\\Classes.");
            }

            var registered = new List<string>(_sourceExtensions.Count);
            foreach (var ext in _sourceExtensions)
            {
                var normExt = ext.Trim().ToLowerInvariant();
                if (!normExt.StartsWith('.')) normExt = "." + normExt;

                var verbPath = $@"SystemFileAssociations\{normExt}\shell\{VerbName}";
                using var verbKey = classesKey.CreateSubKey(verbPath);
                verbKey.SetValue(null, VerbText);
                verbKey.SetValue("Icon", fullExePath);
                verbKey.SetValue("MultiSelectModel", MultiSelectPlayer);

                using var cmdKey = verbKey.CreateSubKey("command");
                cmdKey.SetValue(null, $"\"{fullExePath}\" \"%1\"");

                registered.Add(normExt);
            }

            // Record manifest for safe uninstall
            WriteManifest(new ShellRegistrationManifest
            {
                Version = RegistrationVersion,
                ExecutablePath = fullExePath,
                RegisteredExtensions = registered,
                TimestampUtc = DateTime.UtcNow
            });

            QuickConvertLogger.Info($"Registered QuickConvert shell verbs for {registered.Count} extensions.");
            return ShellOperationResult.Ok($"Successfully registered shell verbs for {registered.Count} extensions.", registered.Count);
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Error("Failed to register Explorer shell verbs.", ex);
            return ShellOperationResult.Fail($"Failed to register shell verbs: {ex.Message}");
        }
    }

    public ShellOperationResult Unregister()
    {
        try
        {
            using var classesKey = _registry.OpenCurrentUserClasses();
            if (classesKey == null)
            {
                return ShellOperationResult.Fail("Unable to access HKCU\\Software\\Classes.");
            }

            // Retrieve registered extensions from manifest, falling back to matrix
            var extensionsToRemove = GetExtensionsToRemove();
            int removedCount = 0;

            foreach (var ext in extensionsToRemove)
            {
                var normExt = ext.Trim().ToLowerInvariant();
                if (!normExt.StartsWith('.')) normExt = "." + normExt;

                var subKeyPath = $@"SystemFileAssociations\{normExt}\shell\{VerbName}";
                try
                {
                    classesKey.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
                    removedCount++;
                }
                catch (Exception ex)
                {
                    QuickConvertLogger.Warn($"Could not delete registry key {subKeyPath}: {ex.Message}");
                }
            }

            // Remove manifest file
            DeleteManifest();

            QuickConvertLogger.Info($"Unregistered QuickConvert shell verbs for {removedCount} extensions.");
            return ShellOperationResult.Ok($"Successfully unregistered shell verbs for {removedCount} extensions.", removedCount);
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Error("Failed to unregister Explorer shell verbs.", ex);
            return ShellOperationResult.Fail($"Failed to unregister shell verbs: {ex.Message}");
        }
    }

    public bool IsRegistered()
    {
        try
        {
            // First check manifest
            if (File.Exists(_manifestPath))
            {
                var manifest = ReadManifest();
                if (manifest?.RegisteredExtensions.Count > 0)
                {
                    return true;
                }
            }

            // Alternatively check registry directly for a sample extension
            using var classesKey = _registry.OpenCurrentUserClasses();
            if (classesKey != null)
            {
                var sample = _sourceExtensions.Count > 0 ? _sourceExtensions[0] : ".jpg";
                using var verbKey = classesKey.OpenSubKey($@"SystemFileAssociations\{sample}\shell\{VerbName}");
                return verbKey != null;
            }
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Failed to query shell registration status: {ex.Message}");
        }

        return false;
    }

    public IReadOnlyList<string> GetRegisteredExtensions()
    {
        var manifest = ReadManifest();
        if (manifest != null && manifest.RegisteredExtensions.Count > 0)
        {
            return manifest.RegisteredExtensions;
        }

        return Array.Empty<string>();
    }

    private IEnumerable<string> GetExtensionsToRemove()
    {
        var manifest = ReadManifest();
        if (manifest != null && manifest.RegisteredExtensions.Count > 0)
        {
            return manifest.RegisteredExtensions;
        }

        return _sourceExtensions;
    }

    private ShellRegistrationManifest? ReadManifest()
    {
        try
        {
            if (File.Exists(_manifestPath))
            {
                var json = File.ReadAllText(_manifestPath);
                return JsonSerializer.Deserialize<ShellRegistrationManifest>(json);
            }
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Could not read shell registration manifest: {ex.Message}");
        }

        return null;
    }

    private void WriteManifest(ShellRegistrationManifest manifest)
    {
        try
        {
            var dir = Path.GetDirectoryName(_manifestPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(manifest, IndentedJson);
            File.WriteAllText(_manifestPath, json);
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Could not write shell registration manifest: {ex.Message}");
        }
    }

    private void DeleteManifest()
    {
        try
        {
            if (File.Exists(_manifestPath))
            {
                File.Delete(_manifestPath);
            }
        }
        catch (Exception ex)
        {
            QuickConvertLogger.Warn($"Could not delete shell registration manifest: {ex.Message}");
        }
    }

    private sealed class UnsupportedPlatformRegistry : IRegistry
    {
        public IRegistryKey? OpenCurrentUserClasses() => null;
    }
}
