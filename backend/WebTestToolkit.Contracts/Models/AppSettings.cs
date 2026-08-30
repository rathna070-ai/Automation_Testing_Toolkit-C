namespace WebTestToolkit.Contracts.Models;

// Persisted to %AppData%\WebTestToolkit\settings.json by the Settings window.
public class AppSettings
{
    public string? GroqApiKey { get; set; }
    public string GroqModel { get; set; } = "openai/gpt-oss-120b";

    // The Groq plan's tokens-per-minute allowance. Groq meters a request as prompt plus the
    // reserved max_completion_tokens together, so this is the ceiling a whole request has to
    // fit under; HybridTestCodeGenerator skips the AI path locally rather than spend a round
    // trip on a request the API would reject with a 413.
    //
    // Defaults to the free tier's 8,000 for gpt-oss-120b, which the assembled prompt bundle
    // exceeds on its own — so on the free tier the AI path is effectively unreachable. Groq's
    // Developer tier is a no-cost upgrade (card on file, pay-per-token) at roughly 250,000–
    // 300,000, and raising this to match is what actually turns AI generation back on. It is
    // a *plan* property, not a model or code property, which is why it lives in settings
    // rather than as a constant.
    public int GroqTokensPerMinute { get; set; } = 8_000;
}
