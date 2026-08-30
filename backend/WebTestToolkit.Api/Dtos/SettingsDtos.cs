namespace WebTestToolkit.Api.Dtos;

// The API key itself never appears in a response — only whether one is set.
public record SettingsResponse(string GroqModel, bool ApiKeyConfigured, int GroqTokensPerMinute);

// GroqApiKey: null = leave the stored key unchanged, "" = clear it, non-empty = set it.
// GroqTokensPerMinute: null = leave unchanged. Matches the Groq plan's allowance —
// 8,000 on the free tier, ~250,000+ on Developer.
public record UpdateSettingsRequest(string? GroqApiKey, string? GroqModel, int? GroqTokensPerMinute = null);
