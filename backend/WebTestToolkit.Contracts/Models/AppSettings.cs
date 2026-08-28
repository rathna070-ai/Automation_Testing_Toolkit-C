namespace WebTestToolkit.Contracts.Models;

// Persisted to %AppData%\WebTestToolkit\settings.json by the Settings window.
public class AppSettings
{
    public string? GroqApiKey { get; set; }
    public string GroqModel { get; set; } = "openai/gpt-oss-120b";
}
