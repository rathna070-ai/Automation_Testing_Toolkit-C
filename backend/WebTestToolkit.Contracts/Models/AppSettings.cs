namespace WebTestToolkit.Contracts.Models;

// Persisted to %AppData%\WebTestToolkit\settings.json by the Settings window.
public class AppSettings
{
    public string? GroqApiKey { get; set; }
    public string GroqModel { get; set; } = "llama-3.3-70b-versatile";
}
