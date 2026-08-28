namespace WebTestToolkit.Llm;

// Resolved at call time, not bound once at startup, so a key saved through the Settings
// page takes effect on the next call without restarting the API.
public record GroqSettings(string? ApiKey, string Model);

public interface IGroqSettingsProvider
{
    Task<GroqSettings> GetAsync(CancellationToken ct = default);
}
