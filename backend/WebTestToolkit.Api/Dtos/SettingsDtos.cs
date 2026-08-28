namespace WebTestToolkit.Api.Dtos;

// The API key itself never appears in a response — only whether one is set.
public record SettingsResponse(string GroqModel, bool ApiKeyConfigured);

// GroqApiKey: null = leave the stored key unchanged, "" = clear it, non-empty = set it.
public record UpdateSettingsRequest(string? GroqApiKey, string? GroqModel);
