using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Services;

// Persists to %AppData%\WebTestToolkit\settings.json. The API key is encrypted at rest via
// Windows DPAPI (CurrentUser scope) before it touches disk — this is obfuscation tied to the
// logged-in Windows account, not a real secrets vault, and the Settings UI should say so.
// GROQ_API_KEY, if set, is used only when no key has been saved through the UI yet.
[SupportedOSPlatform("windows")]
public class FileSettingsStore : ISettingsStore
{
    private static readonly byte[] Entropy = "WebTestToolkit.settings.v1"u8.ToArray();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<FileSettingsStore> _logger;
    private readonly string _filePath;

    public FileSettingsStore(ILogger<FileSettingsStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WebTestToolkit");
        _filePath = Path.Combine(dir, "settings.json");
    }

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var settings = ReadFromDisk();
            if (string.IsNullOrWhiteSpace(settings.GroqApiKey))
                settings.GroqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);

            var persisted = new PersistedSettingsFile
            {
                GroqApiKeyProtected = string.IsNullOrEmpty(settings.GroqApiKey) ? null : Protect(settings.GroqApiKey),
                GroqModel = settings.GroqModel,
                GroqTokensPerMinute = settings.GroqTokensPerMinute
            };

            var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private AppSettings ReadFromDisk()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            var persisted = JsonSerializer.Deserialize<PersistedSettingsFile>(json);
            if (persisted is null)
                return new AppSettings();

            return new AppSettings
            {
                GroqApiKey = persisted.GroqApiKeyProtected is null ? null : Unprotect(persisted.GroqApiKeyProtected),
                GroqModel = string.IsNullOrWhiteSpace(persisted.GroqModel)
                    ? new AppSettings().GroqModel
                    : persisted.GroqModel,
                // 0 means the field predates this setting (a settings file written before it
                // existed), not a deliberate "no allowance" — fall back to the default.
                GroqTokensPerMinute = persisted.GroqTokensPerMinute > 0
                    ? persisted.GroqTokensPerMinute
                    : new AppSettings().GroqTokensPerMinute
            };
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or IOException)
        {
            _logger.LogWarning(ex, "Could not read settings file at {Path}; using defaults", _filePath);
            return new AppSettings();
        }
    }

    private static string Protect(string plaintext)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string protectedBase64)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    // The on-disk shape, deliberately separate from AppSettings so the key can be stored
    // protected rather than plain. That separation means a field added to AppSettings is
    // silently dropped on save until it is added here too — which is exactly what happened
    // to GroqTokensPerMinute: the PUT response echoed the new value while the next GET
    // returned the default, because the round trip through this class lost it.
    private class PersistedSettingsFile
    {
        public string? GroqApiKeyProtected { get; set; }
        public string GroqModel { get; set; } = "";
        public int GroqTokensPerMinute { get; set; }
    }
}
