namespace WebTestToolkit.Contracts.Models;

// Result of asking Groq to explain a ScenarioResult's failure.
public class FailureAnalysis
{
    public string RootCause { get; set; } = "";
    public string SuggestedFix { get; set; } = "";
    public string RawModelResponse { get; set; } = "";
}
